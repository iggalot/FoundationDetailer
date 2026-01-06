using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FoundationDetailer.AutoCAD;
using FoundationDetailer.Model;
using FoundationDetailsLibraryAutoCAD.AutoCAD;
using FoundationDetailsLibraryAutoCAD.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace FoundationDetailer.Managers
{
    /// <summary>
    /// Robust manager for a single polyline "boundary" per Document.
    /// Tracks modifications, erases, undo/redo, append/unappend, and command-based edits.
    /// Adds hybrid handle-change / replacement detection and automatic adoption of replacement polylines.
    /// </summary>
    public class PolylineBoundaryManager
    {
        private const string XrecordKey = "FD_BOUNDARY";

        // Document -> stored ObjectId (ObjectId.Null if none)
        private static readonly ConcurrentDictionary<Document, ObjectId> _docBoundaryIds = new ConcurrentDictionary<Document, ObjectId>();

        // Document -> last captured geometry snapshot (used to detect replacements)
        private static readonly ConcurrentDictionary<Document, GeometrySnapshot> _lastSnapshots = new ConcurrentDictionary<Document, GeometrySnapshot>();

        // Document -> command-scoped state (appended/erased objects observed during a command)
        private static readonly ConcurrentDictionary<Document, CommandState> _docCommandStates = new ConcurrentDictionary<Document, CommandState>();

        // Per-document deferred idle handlers
        private static readonly ConcurrentDictionary<Document, EventHandler> _deferredIdleHandlers = new ConcurrentDictionary<Document, EventHandler>();

        // Commands worth watching (kept uppercase)
        private static readonly HashSet<string> _monitoredCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MOVE", "STRETCH", "GRIP_STRETCH", "PEDIT", "EXPLODE", "GRIPS", "GRIP_MOVE",
            "ROTATE", "SCALE", "MIRROR", "ERASE", "DELETE", /*"U", "UNDO", "REDO",*/ "JOIN"
        };

        // Commands that often *replace* geometry (we'll attempt replacement detection after these)
        private static readonly HashSet<string> _replacementCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PEDIT", "EXPLODE", "JOIN"
        };

        public static event EventHandler BoundaryChanged;

        /// <summary>
        /// Raises the BoundaryChanged event to notify subscribers.
        /// </summary>
        public static void RaiseBoundaryChanged()
        {
            BoundaryChanged?.Invoke(null, EventArgs.Empty);
        }

        public PolylineBoundaryManager()
        {
        }

        #region NOD Helpers
        /// <summary>
        /// Function to add a polyline boundary handle to the NOD
        /// </summary>
        /// <param name="id"></param>
        internal static void AddBoundaryHandleToNOD(FoundationContext context, Transaction tr, ObjectId id)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (id.IsNull) return;

            var doc = context.Document;
            var db = doc.Database;

            // Ensure NOD structure exists
            NODManager.InitFoundationNOD(context, tr);

            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            var root = (DBDictionary)tr.GetObject(nod.GetAt(NODManager.ROOT), OpenMode.ForWrite);
            var boundaryDict = (DBDictionary)tr.GetObject(root.GetAt(NODManager.KEY_BOUNDARY), OpenMode.ForWrite);

            string handleStr = id.Handle.ToString().ToUpperInvariant();
            NODManager.AddHandleToDictionary(tr, boundaryDict, handleStr);

        }

        #endregion

        #region Initialization & Attach/Detach

        public void Initialize(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;


            // Attach to existing documents
            AttachDocumentEvents(context);

            // Document lifecycle events
            var dm = Application.DocumentManager;
            dm.DocumentCreated -= (s, e) => DocManager_DocumentCreated(s, e, context);
            dm.DocumentCreated += (s, e) => DocManager_DocumentCreated(s, e, context);

            dm.DocumentToBeDestroyed -= (s, e) => DocManager_DocumentToBeDestroyed(s, e, context);
            dm.DocumentToBeDestroyed += (s, e) => DocManager_DocumentToBeDestroyed(s, e, context);

            dm.DocumentActivated -= (s, e) => DocManager_DocumentActivated(s, e, context);
            dm.DocumentActivated += (s, e) => DocManager_DocumentActivated(s, e, context);

            // Load for active document (deferred to be safe)
            LoadBoundaryForActiveDocument(context);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Required by event handler signature")]
        private void DocManager_DocumentCreated(object sender, DocumentCollectionEventArgs e, FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            AttachDocumentEvents(context);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Required by event handler signature")]
        private void DocManager_DocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e, FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            DetachDocumentEvents(context);
            _docBoundaryIds.TryRemove(context.Document, out _);
            _lastSnapshots.TryRemove(context.Document, out _);
            _docCommandStates.TryRemove(context.Document, out _);

            if (_deferredIdleHandlers.TryRemove(e.Document, out var existing))
            {
                Application.Idle -= existing;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Required by event handler signature")]
        private void DocManager_DocumentActivated(object sender, DocumentCollectionEventArgs e, FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Defer loading to idle so we don't start transactions inside activation handlers
            DeferActionForDocument(context, () =>
            {
                LoadBoundary(context);
                BoundaryChanged?.Invoke(null, EventArgs.Empty);
            });
        }

        private void AttachDocumentEvents(FoundationContext context)
        {
            var doc = context.Document;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            // Defensive: remove then add to avoid duplicates
            db.ObjectErased -= Db_ObjectErased;
            db.ObjectErased += Db_ObjectErased;

            db.ObjectAppended -= Db_ObjectAppended;
            db.ObjectAppended += Db_ObjectAppended;

            db.ObjectModified -= Db_ObjectModified;
            db.ObjectModified += Db_ObjectModified;

            db.ObjectOpenedForModify -= Db_ObjectOpenedForModify;
            db.ObjectOpenedForModify += Db_ObjectOpenedForModify;

            db.ObjectUnappended -= Db_ObjectUnappended;
            db.ObjectUnappended += Db_ObjectUnappended;

            // Note: ObjectReappended may exist in some API versions; ObjectAppended covers most creation cases.

            doc.CommandWillStart -= Doc_CommandWillStart;
            doc.CommandWillStart += Doc_CommandWillStart;

            doc.CommandEnded -= Doc_CommandEnded;
            doc.CommandEnded += Doc_CommandEnded;

            doc.CommandCancelled -= Doc_CommandCancelled;
            doc.CommandCancelled += Doc_CommandCancelled;

            // Initialize command state holder for this document
            _docCommandStates.AddOrUpdate(doc, new CommandState(), (d, old) => { old.Clear(); return old; });

            // If this doc is active, attempt to load its stored boundary now (deferred)
            DeferActionForDocument(context, () => LoadBoundary(context));
        }

        private static void DetachDocumentEvents(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;

            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            db.ObjectErased -= Db_ObjectErased;
            db.ObjectAppended -= Db_ObjectAppended;
            db.ObjectModified -= Db_ObjectModified;
            db.ObjectOpenedForModify -= Db_ObjectOpenedForModify;
            db.ObjectUnappended -= Db_ObjectUnappended;

            doc.CommandWillStart -= Doc_CommandWillStart;
            doc.CommandEnded -= Doc_CommandEnded;
            doc.CommandCancelled -= Doc_CommandCancelled;

            if (_deferredIdleHandlers.TryRemove(doc, out var existing))
            {
                Application.Idle -= existing;
            }
        }

        #endregion

        #region Public API (original functionality preserved)

        public bool TrySetBoundary(FoundationContext context, ObjectId candidateId, out string error)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (candidateId == null) throw new ArgumentNullException(nameof(candidateId));

            var doc = context.Document;
            var model = context.Model;
            var db = doc.Database;

            error = string.Empty;

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        // Validate entity
                        var ent = tr.GetObject(candidateId, OpenMode.ForRead, false) as Polyline;
                        if (ent == null)
                        {
                            error = "Selected object is not a polyline.";
                            return false;
                        }

                        // Normalize polyline
                        ent.UpgradeOpen();
                        EnsureClosedAndCCW(ent);
                        ent.DowngradeOpen();

                        // Persist handle via NODManager
                        FoundationEntityData.Write(tr, ent, NODManager.KEY_BOUNDARY);
                        AddBoundaryHandleToNOD(context, tr, candidateId);

                        // Update in-memory map
                        _docBoundaryIds.AddOrUpdate(doc, candidateId, (d, old) => candidateId);

                        tr.Commit();

                        BoundaryChanged?.Invoke(null, EventArgs.Empty);
                        return true;
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        error = ex.Message;
                        return false;
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                        return false;
                    }
                }
            }
        }


        /// <summary>
        /// Highlight all grade beams in the current document.
        /// </summary>
        public void HighlightBoundary(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            if (doc == null) return;

            if (!TryGetBoundary(context, out Polyline pl))
                return;

            // Bring AutoCAD to front and highlight selected objects
            SelectionService.FocusAndHighlight(context, new[] { pl.ObjectId }, "HighlightGradeBeam");
        }

        public bool TryGetBoundary(FoundationContext context, out Polyline pl)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;

            pl = null;

            if (doc == null) return false;

            var db = doc.Database;

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // Delegate the dictionary and handle lookup to NODManager
                    if (!NODManager.TryGetFirstEntity(context, tr, db, NODManager.KEY_BOUNDARY, out ObjectId oid))
                        return false;

                    if (oid.IsNull || oid.IsErased || !oid.IsValid)
                        return false;

                    pl = tr.GetObject(oid, OpenMode.ForRead, false) as Polyline;
                    return pl != null;
                }
            }
            catch
            {
                return false;
            }
        }





        public void ZoomToBoundary(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;

            if (doc == null) return;

            var ed = doc.Editor;

            if (!TryGetBoundary(context, out Polyline pl)) return;

            try
            {
                using (doc.LockDocument())
                {
                    ed.SetImpliedSelection(new ObjectId[] { pl.ObjectId });
                    Extents3d ext = pl.GeometricExtents;
                    if (ext.MinPoint.DistanceTo(ext.MaxPoint) < 1e-6) return;

                    var view = ed.GetCurrentView();
                    Point2d center = new Point2d(
                        (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                        (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0
                    );

                    double width = ext.MaxPoint.X - ext.MinPoint.X;
                    double height = ext.MaxPoint.Y - ext.MinPoint.Y;
                    if (width < 1e-6) width = 1.0;
                    if (height < 1e-6) height = 1.0;

                    view.CenterPoint = center;
                    view.Width = width * 1.1;
                    view.Height = height * 1.1;

                    ed.SetCurrentView(view);
                }
            }
            catch { }
        }

        public static void ClearBoundaryForActiveDocument(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;

            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    var nod = (DBDictionary)tr.GetObject(doc.Database.NamedObjectsDictionaryId, OpenMode.ForWrite);
                    if (nod.Contains(XrecordKey))
                    {
                        var xrId = nod.GetAt(XrecordKey);
                        var xr = tr.GetObject(xrId, OpenMode.ForWrite) as Xrecord;
                        if (xr != null)
                        {
                            xr.Data = null;
                        }

                        nod.Remove(XrecordKey);
                    }

                    tr.Commit();
                }

                _docBoundaryIds.AddOrUpdate(doc, ObjectId.Null, (d, old) => ObjectId.Null);
                _lastSnapshots.TryRemove(doc, out _);
                BoundaryChanged?.Invoke(null, EventArgs.Empty);
            }
            catch { }
        }

        #endregion

        #region Event Handlers (robust set)

        private static void Db_ObjectAppended(object sender, ObjectEventArgs e)
        {
            var db = sender as Database;
            if (db == null) return;

            var doc = Application.DocumentManager.GetDocument(db);
            if (doc == null) return;

            var context = FoundationContext.For(doc);
            if (context == null) return;

            DeferActionForDocument(context, () => CheckBoundaryForDocument(context));
        }

        private static void Db_ObjectErased(object sender, ObjectErasedEventArgs e)
        {
            var db = sender as Database;
            if (db == null) return;

            var doc = Application.DocumentManager.GetDocument(db);
            if (doc == null) return;

            var context = FoundationContext.For(doc);
            if (context == null) return;

            DeferActionForDocument(context, () => CheckBoundaryForDocument(context));
        }

        private static void CheckBoundaryForDocument(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;

            if (doc == null) return;

            var ed = doc.Editor;

            try
            {
                Database db = doc.Database;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // ------------------------------
                    // 1. Retrieve FD_BOUNDARY dictionary
                    // ------------------------------
                    DBDictionary nod =
                        (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

                    if (!nod.Contains(NODManager.ROOT))
                    {
                        tr.Commit();
                        return;
                    }

                    DBDictionary root =
                        (DBDictionary)tr.GetObject(nod.GetAt(NODManager.ROOT), OpenMode.ForRead);

                    if (!root.Contains(NODManager.KEY_BOUNDARY))
                    {
                        tr.Commit();
                        return;
                    }

                    DBDictionary boundaryDict =
                        (DBDictionary)tr.GetObject(root.GetAt(NODManager.KEY_BOUNDARY), OpenMode.ForRead);

                    // ------------------------------
                    // 2. Expect exactly ONE boundary entry
                    // ------------------------------
                    if (boundaryDict.Count == 0)
                    {
                        tr.Commit();
                        return;
                    }

                    // Get the *first* (and only expected) boundary handle
                    var entry = boundaryDict.Cast<DBDictionaryEntry>().First();
                    string handleStr = entry.Key;

                    // ------------------------------
                    // 3. Convert handle → ObjectId
                    // ------------------------------
                    if (!NODManager.TryGetObjectIdFromHandleString(context, db, handleStr, out ObjectId boundaryId) ||
                        boundaryId.IsNull)
                    {
                        tr.Commit();
                        return;
                    }

                    // ------------------------------
                    // 4. Validate that the boundary entity exists and is not erased
                    // ------------------------------
                    Entity ent = null;

                    try
                    {
                        ent = tr.GetObject(boundaryId, OpenMode.ForRead) as Entity;
                    }
                    catch
                    {
                        ent = null;
                    }

                    if (ent == null || ent.IsErased)
                    {
                        tr.Commit();
                        return;
                    }

                    // ------------------------------
                    // 5. Store in internal dictionary + notify listeners
                    // ------------------------------
                    _docBoundaryIds.AddOrUpdate(doc, boundaryId, (d, old) => boundaryId);

                    BoundaryChanged?.Invoke(null, EventArgs.Empty);

                    tr.Commit();
                }
            }
            catch
            {
                // Fail silently (matching your existing behavior)
            }
        }


        private static void Db_ObjectUnappended(object sender, ObjectEventArgs e)
        {
            var db = sender as Database;
            if (db == null) return;

            var doc = Application.DocumentManager.GetDocument(db);
            if (doc == null) return;

            var context = FoundationContext.For(doc);
            if (context == null) return;

            if (e.DBObject == null) return;

            // treat as erased (object removed from DB)
            if (_docCommandStates.TryGetValue(doc, out CommandState state))
            {
                state.ErasedIds.Add(e.DBObject.ObjectId);
            }

            if (_docBoundaryIds.TryGetValue(doc, out ObjectId stored) && stored == e.DBObject.ObjectId)
            {
                _docBoundaryIds.AddOrUpdate(doc, ObjectId.Null, (d, old) => ObjectId.Null);
                BoundaryChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        private static void Db_ObjectModified(object sender, ObjectEventArgs e)
        {
            var db = sender as Database;
            if (db == null) return;

            var doc = Application.DocumentManager.GetDocument(db);
            if (doc == null) return;

            var context = FoundationContext.For(doc);
            if (context == null) return;

            DeferActionForDocument(context, () => CheckBoundaryForDocument(context));
        }


        private static void Db_ObjectOpenedForModify(object sender, ObjectEventArgs e)
        {
            var db = sender as Database;
            if (db == null) return;

            var doc = Application.DocumentManager.GetDocument(db);
            if (doc == null) return;

            var context = FoundationContext.For(doc);
            if (context == null) return;

            if (e.DBObject == null) return;

            // If the stored boundary is being opened for modify, capture a snapshot for later replacement detection
            if (_docBoundaryIds.TryGetValue(doc, out ObjectId stored) && stored == e.DBObject.ObjectId)
            {
                try
                {
                    using (doc.LockDocument())
                    using (var tr = doc.TransactionManager.StartTransaction())
                    {
                        if (e.DBObject.IsErased) { tr.Commit(); return; }

                        var pl = tr.GetObject(e.DBObject.ObjectId, OpenMode.ForRead, false) as Polyline;
                        if (pl != null)
                        {
                            var snap = GeometrySnapshot.FromPolyline(pl);
                            _lastSnapshots.AddOrUpdate(doc, snap, (d, old) => snap);
                        }

                        tr.Commit();
                    }
                }
                catch { /* ignore snapshot failures */ }
            }
        }

        private static void Doc_CommandWillStart(object sender, CommandEventArgs e)
        {
            var db = sender as Database;
            if (db == null) return;

            var doc = Application.DocumentManager.GetDocument(db);
            if (doc == null) return;

            var context = FoundationContext.For(doc);
            if (context == null) return;

            if (e == null || string.IsNullOrEmpty(e.GlobalCommandName)) return;

            var cmd = e.GlobalCommandName.ToUpperInvariant();

            // Reset command state for this document (start new command cycle)
            var state = _docCommandStates.GetOrAdd(doc, new CommandState());
            state.Clear();
            state.CurrentCommand = cmd;

            if (_monitoredCommands.Contains(cmd))
            {
                // Some listeners may want to know a monitored command is starting
                BoundaryChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        private static void Doc_CommandEnded(object sender, CommandEventArgs e)
        {
            var db = sender as Database;
            if (db == null) return;

            var doc = Application.DocumentManager.GetDocument(db);
            if (doc == null) return;

            var context = FoundationContext.For(doc);
            if (context == null) return;


            if (e == null || string.IsNullOrEmpty(e.GlobalCommandName)) return;

            var cmd = e.GlobalCommandName.ToUpperInvariant();

            // If monitored, reload XRecord and notify (deferred)
            if (_monitoredCommands.Contains(cmd))
            {
                DeferActionForDocument(context, () =>
                {
                    LoadBoundary(context);
                    BoundaryChanged?.Invoke(null, EventArgs.Empty);
                });
            }

            // Hybrid replacement detection:
            // 1) If replacement-prone command, attempt to adopt from appended objects observed in this command.
            // 2) If not found, fallback to scanning all polylines against last snapshot.
            if (_replacementCommands.Contains(cmd))
            {
                if (_docCommandStates.TryGetValue(doc, out CommandState state))
                {
                    // Capture a copy of appended ids NOW (state may be cleared later)
                    var appendedCopy = state.AppendedIds.ToList();

                    DeferActionForDocument(context, () =>
                    {
                        bool adopted = false;

                        if (appendedCopy.Count > 0)
                        {
                            try
                            {
                                using (var tr = doc.TransactionManager.StartTransaction())
                                {
                                    foreach (var oid in appendedCopy)
                                    {
                                        if (oid.IsNull || oid.IsErased) continue;
                                        try
                                        {
                                            var ent = tr.GetObject(oid, OpenMode.ForRead, false) as Polyline;
                                            if (ent != null)
                                            {
                                                // If snapshot exists, match by similarity; otherwise match by geometry heuristics
                                                if (_lastSnapshots.TryGetValue(doc, out var snap))
                                                {
                                                    if (GeometrySnapshot.IsSimilar(ent, snap))
                                                    {
                                                        AdoptReplacement(doc, tr, ent.ObjectId);
                                                        adopted = true;
                                                        break;
                                                    }
                                                }
                                                else
                                                {
                                                    // No snapshot; try simple heuristics: vertex count equal or close and extents overlap
                                                    if (TrySimpleHeuristicMatch(doc, tr, ent))
                                                    {
                                                        AdoptReplacement(doc, tr, ent.ObjectId);
                                                        adopted = true;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        catch { /* ignore per-entity errors */ }
                                    }

                                    tr.Commit();
                                }
                            }
                            catch { /* ignore */ }
                        }

                        if (!adopted)
                        {
                            TryAdoptReplacementByGeometry(doc, ObjectId.Null);
                        }

                        // Clear command state after processing (safely)
                        state.Clear();
                    });
                }
            }
            else
            {
                // For non-replacement commands, also consider that appended objects could match (rare)
                if (_docCommandStates.TryGetValue(doc, out CommandState st))
                {
                    // Capture copy of appended ids now
                    var appendedCopy = st.AppendedIds.ToList();

                    DeferActionForDocument(context, () =>
                    {
                        try
                        {
                            // Quick attempt: if stored id is null but appended list contains a matching polyline, adopt it
                            if ((_docBoundaryIds.TryGetValue(doc, out ObjectId stored) && stored.IsNull) && appendedCopy.Count > 0)
                            {
                                using (var tr = doc.TransactionManager.StartTransaction())
                                {
                                    foreach (var oid in appendedCopy)
                                    {
                                        if (oid.IsNull || oid.IsErased) continue;
                                        try
                                        {
                                            var ent = tr.GetObject(oid, OpenMode.ForRead, false) as Polyline;
                                            if (ent != null)
                                            {
                                                if (_lastSnapshots.TryGetValue(doc, out var snap))
                                                {
                                                    if (GeometrySnapshot.IsSimilar(ent, snap))
                                                    {
                                                        AdoptReplacement(doc, tr, ent.ObjectId);
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                        catch { }
                                    }

                                    tr.Commit();
                                }
                            }
                        }
                        catch { }

                        st.Clear();
                    });
                }
            }
        }

        private static void Doc_CommandCancelled(object sender, CommandEventArgs e)
        {
            var db = sender as Database;
            if (db == null) return;

            var doc = Application.DocumentManager.GetDocument(db);
            if (doc == null) return;

            var context = FoundationContext.For(doc);
            if (context == null) return;

            if (doc != null)
            {
                DeferActionForDocument(context, () => LoadBoundary(context));
            }

            DeferActionForDocument(context, () => BoundaryChanged?.Invoke(null, EventArgs.Empty));
        }

        #endregion

        #region Helper Methods & Replacement Detection

        private static Document TryGetDocumentForDatabase(Database db)
        {
            if (db == null) return null;
            foreach (Document d in Application.DocumentManager)
            {
                if (d.Database == db) return d;
            }
            return null;
        }

        private void LoadBoundaryForActiveDocument(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var model = context.Model;

            if (doc != null)
                DeferActionForDocument(context, () => LoadBoundary(context));
        }

        /// <summary>
        /// Load the boundary for the specific document from the NamedObjectsDictionary XRecord (if exists).
        /// Updates the in-memory stored ObjectId.
        /// </summary>
        private static void LoadBoundary(FoundationContext context)
        {
            var doc = context.Document;
            var model = context.Model;

            if (doc == null) return;
            try
            {
                using (doc.LockDocument())
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    var db = doc.Database;
                    var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                    if (!nod.Contains(XrecordKey))
                    {
                        _docBoundaryIds.AddOrUpdate(doc, ObjectId.Null, (d, old) => ObjectId.Null);
                        tr.Commit();
                        return;
                    }

                    var xr = (Xrecord)tr.GetObject(nod.GetAt(XrecordKey), OpenMode.ForRead);
                    if (xr?.Data == null || xr.Data.AsArray().Length == 0)
                    {
                        _docBoundaryIds.AddOrUpdate(doc, ObjectId.Null, (d, old) => ObjectId.Null);
                        tr.Commit();
                        return;
                    }

                    var arr = xr.Data.AsArray();
                    var tv = arr[0];
                    if (tv == null)
                    {
                        _docBoundaryIds.AddOrUpdate(doc, ObjectId.Null, (d, old) => ObjectId.Null);
                        tr.Commit();
                        return;
                    }

                    ObjectId oid = ObjectId.Null;

                    // If stored as a handle string, try parsing robustly.
                    try
                    {
                        var s = tv.Value as string ?? tv.Value.ToString();
                        if (NODManager.TryParseHandle(context, s, out Handle h))
                        {
                            oid = doc.Database.GetObjectId(false, h, 0);
                        }
                        else if (tv.TypeCode == (int)DxfCode.SoftPointerId && tv.Value is ObjectId tvOid)
                        {
                            oid = tvOid;
                        }
                    }
                    catch
                    {
                        oid = ObjectId.Null;
                    }

                    _docBoundaryIds.AddOrUpdate(doc, oid, (d, old) => oid);
                    tr.Commit();
                }
            }
            catch
            {
                _docBoundaryIds.AddOrUpdate(doc, ObjectId.Null, (d, old) => ObjectId.Null);
            }
        }

        /// <summary>
        /// Store the ObjectId (via its handle string) into NamedObjectsDictionary / XRecord
        /// </summary>
        private static void StorePolylineId(Document doc, ObjectId id, Transaction tr)
        {
            if (doc == null || tr == null) throw new ArgumentNullException();
            var db = doc.Database;

            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            Xrecord xr;
            if (nod.Contains(XrecordKey))
            {
                xr = (Xrecord)tr.GetObject(nod.GetAt(XrecordKey), OpenMode.ForWrite) as Xrecord;
            }
            else
            {
                xr = new Xrecord();
                nod.SetAt(XrecordKey, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }

            // Save the handle string so conversion later is robust
            var handleString = id.Handle.ToString();
            xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Handle, handleString));
        }

        /// <summary>
        /// Try to adopt a replacement polyline by geometric similarity.
        /// If candidateOid == ObjectId.Null then we will scan all polylines; otherwise examine only candidateOid.
        /// </summary>
        private static void TryAdoptReplacementByGeometry(Document doc, ObjectId candidateOid)
        {
            if (doc == null) return;

            if (!_lastSnapshots.TryGetValue(doc, out GeometrySnapshot snap)) return; // nothing to match against

            try
            {
                using (doc.LockDocument())
                using (var tr = doc.TransactionManager.StartTransaction())
                {
                    var db = doc.Database;
                    // If a single candidate provided, evaluate it
                    if (candidateOid != ObjectId.Null && candidateOid.IsValid && !candidateOid.IsErased)
                    {
                        var ent = tr.GetObject(candidateOid, OpenMode.ForRead, false) as Polyline;
                        if (ent != null && GeometrySnapshot.IsSimilar(ent, snap))
                        {
                            AdoptReplacement(doc, tr, ent.ObjectId);
                            tr.Commit();
                            return;
                        }
                    }
                    else
                    {
                        // Scan all polylines in modelspace for a match
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                        foreach (ObjectId entId in btr)
                        {
                            if (entId.IsNull || entId.IsErased) continue;
                            var ent = tr.GetObject(entId, OpenMode.ForRead, false) as Polyline;
                            if (ent == null) continue;

                            if (GeometrySnapshot.IsSimilar(ent, snap))
                            {
                                AdoptReplacement(doc, tr, ent.ObjectId);
                                tr.Commit();
                                return;
                            }
                        }

                        // Optionally search paper space block tables as well if needed (omitted for performance)
                    }

                    tr.Commit();
                }
            }
            catch
            {
                // ignore detection failures
            }
            finally
            {
                // Whether we found a replacement or not, clear snapshot to avoid repeated scans
                _lastSnapshots.TryRemove(doc, out _);
            }
        }

        /// <summary>
        /// Adopt the provided ObjectId as the new stored boundary (update XRecord & in-memory map).
        /// Assumes a transaction is active.
        /// </summary>
        private static void AdoptReplacement(Document doc, Transaction tr, ObjectId newId)
        {
            if (doc == null || tr == null) return;

            try
            {
                var db = doc.Database;
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                Xrecord xr;
                if (nod.Contains(XrecordKey))
                {
                    xr = (Xrecord)tr.GetObject(nod.GetAt(XrecordKey), OpenMode.ForWrite) as Xrecord;
                }
                else
                {
                    xr = new Xrecord();
                    nod.SetAt(XrecordKey, xr);
                    tr.AddNewlyCreatedDBObject(xr, true);
                }

                xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Handle, newId.Handle.ToString()));

                _docBoundaryIds.AddOrUpdate(doc, newId, (d, old) => newId);

                // Remove any saved snapshot (we found the replacement)
                _lastSnapshots.TryRemove(doc, out _);

                BoundaryChanged?.Invoke(null, EventArgs.Empty);
            }
            catch
            {
                // ignore adopt failures (we don't want to throw from event handlers)
            }
        }

        /// <summary>
        /// Simple heuristic match when no snapshot: vertex count equality and overlapping extents
        /// </summary>
        private static bool TrySimpleHeuristicMatch(Document doc, Transaction tr, Polyline candidate)
        {
            if (doc == null || tr == null || candidate == null) return false;

            try
            {
                if (!_docBoundaryIds.TryGetValue(doc, out ObjectId prev) || prev.IsNull) return false;

                if (prev.IsErased || !prev.IsValid) return false;

                // Try to get the previous polyline object for quick heuristics
                var prevEnt = tr.GetObject(prev, OpenMode.ForRead, false) as Polyline;
                if (prevEnt == null) return false;

                if (Math.Abs(prevEnt.NumberOfVertices - candidate.NumberOfVertices) > 2) return false;

                var extPrev = prevEnt.GeometricExtents;
                var extCand = candidate.GeometricExtents;

                // extents overlap test
                var prevRect = new Rectangle(extPrev.MinPoint, extPrev.MaxPoint);
                var candRect = new Rectangle(extCand.MinPoint, extCand.MaxPoint);

                if (!prevRect.Overlaps(candRect)) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Geometry Snapshot Helper (lightweight similarity checks)

        /// <summary>
        /// Lightweight geometry snapshot extracted from a polyline: vertex count, centroid, polygon area, and extents size.
        /// </summary>
        private class GeometrySnapshot
        {
            public int VertexCount { get; private set; }
            public Point2d Centroid { get; private set; }
            public double Area { get; private set; }           // absolute area
            public double ExtentsWidth { get; private set; }
            public double ExtentsHeight { get; private set; }

            public static GeometrySnapshot FromPolyline(Polyline pl)
            {
                var snap = new GeometrySnapshot
                {
                    VertexCount = pl.NumberOfVertices
                };

                // centroid & area via shoelace (2D)
                var pts = new List<Point2d>(pl.NumberOfVertices);
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    pts.Add(pl.GetPoint2dAt(i));
                }

                // Calculate area and centroid
                double area = 0;
                double cx = 0, cy = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    double cross = a.X * b.Y - b.X * a.Y;
                    area += cross;
                    cx += (a.X + b.X) * cross;
                    cy += (a.Y + b.Y) * cross;
                }

                area *= 0.5;
                double absArea = Math.Abs(area);
                snap.Area = absArea;

                if (Math.Abs(area) > 1e-9)
                {
                    cx /= (6.0 * area);
                    cy /= (6.0 * area);
                    snap.Centroid = new Point2d(cx, cy);
                }
                else
                {
                    // fallback centroid = average points
                    double avgX = pts.Average(p => p.X);
                    double avgY = pts.Average(p => p.Y);
                    snap.Centroid = new Point2d(avgX, avgY);
                }

                var ext = pl.GeometricExtents;
                snap.ExtentsWidth = Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                snap.ExtentsHeight = Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);

                return snap;
            }

            /// <summary>
            /// Compare a candidate polyline to the snapshot with tolerances.
            /// </summary>
            public static bool IsSimilar(Polyline candidate, GeometrySnapshot snap)
            {
                if (candidate == null || snap == null) return false;

                // Vertex count must be reasonably close (exact is best)
                if (Math.Abs(candidate.NumberOfVertices - snap.VertexCount) > 2)
                    return false;

                // Compute candidate area and centroid quickly
                var pts = new List<Point2d>(candidate.NumberOfVertices);
                for (int i = 0; i < candidate.NumberOfVertices; i++)
                    pts.Add(candidate.GetPoint2dAt(i));

                double area = 0;
                double cx = 0, cy = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    double cross = a.X * b.Y - b.X * a.Y;
                    area += cross;
                    cx += (a.X + b.X) * cross;
                    cy += (a.Y + b.Y) * cross;
                }

                area *= 0.5;
                double absArea = Math.Abs(area);

                // Tolerance rules:
                // - area within 2% or absolute 1.0 unit (whichever larger)
                double areaTol = Math.Max(Math.Abs(snap.Area) * 0.02, 1.0);
                if (Math.Abs(absArea - snap.Area) > areaTol) return false;

                // centroid within reasonable tolerance (based on extents)
                Point2d candCentroid;
                if (Math.Abs(area) > 1e-9)
                {
                    cx /= (6.0 * area);
                    cy /= (6.0 * area);
                    candCentroid = new Point2d(cx, cy);
                }
                else
                {
                    candCentroid = new Point2d(pts.Average(p => p.X), pts.Average(p => p.Y));
                }

                double dist = Math.Sqrt(
                    Math.Pow(candCentroid.X - snap.Centroid.X, 2) +
                    Math.Pow(candCentroid.Y - snap.Centroid.Y, 2)
                );

                double centTol = Math.Max(snap.ExtentsWidth, snap.ExtentsHeight) * 0.1 + 0.5; // 10% of size + 0.5 units
                if (dist > centTol) return false;

                // extents width/height should be similar within 10-12%
                var ext = candidate.GeometricExtents;
                double w = Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                double h = Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);
                if (Math.Abs(w - snap.ExtentsWidth) > Math.Max(snap.ExtentsWidth * 0.12, 0.5)) return false;
                if (Math.Abs(h - snap.ExtentsHeight) > Math.Max(snap.ExtentsHeight * 0.12, 0.5)) return false;

                return true;
            }
        }

        #endregion

        #region Misc Helpers (original geometry fixes)

        private static void EnsureClosedAndCCW(Polyline pl)
        {
            if (pl == null) return;

            try
            {
                if (!pl.Closed)
                    pl.Closed = true;

                if (!IsCounterClockwise(pl))
                {
                    pl.ReverseCurve();
                }
            }
            catch
            {
                // ignore
            }
        }

        private static bool IsCounterClockwise(Polyline pl)
        {
            if (pl == null || pl.NumberOfVertices < 3) return true;
            double sum = 0;
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d a = pl.GetPoint2dAt(i);
                Point2d b = pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices);
                sum += (b.X - a.X) * (b.Y + a.Y);
            }
            return sum < 0;
        }

        private static ObjectId GetBoundaryFromXRecord(FoundationContext context, Transaction tr)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;

            if (doc == null || tr == null) return ObjectId.Null;

            try
            {
                var db = doc.Database;

                // Open the Named Objects Dictionary
                var nod = tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead) as DBDictionary;
                if (nod == null || !nod.Contains(XrecordKey))
                    return ObjectId.Null;

                // Open the XRecord
                var xr = tr.GetObject(nod.GetAt(XrecordKey), OpenMode.ForRead) as Xrecord;
                if (xr?.Data == null) return ObjectId.Null;

                var arr = xr.Data.AsArray();
                if (arr.Length == 0)
                    return ObjectId.Null;

                var tv = arr[0];

                // Convert handle string or ObjectId
                try
                {
                    if (tv.TypeCode == (int)DxfCode.Handle)
                    {
                        var s = tv.Value as string ?? tv.Value.ToString();
                        if (NODManager.TryParseHandle(context, s, out Handle h))
                        {
                            return db.GetObjectId(false, h, 0);
                        }
                    }
                    else if (tv.TypeCode == (int)DxfCode.SoftPointerId && tv.Value is ObjectId oid)
                    {
                        return oid;
                    }
                }
                catch { }

                return ObjectId.Null;
            }
            catch
            {
                return ObjectId.Null;
            }
        }



        #endregion

        #region Nested helpers & small utility types

        // Per-document command-scoped tracking
        private class CommandState
        {
            public string CurrentCommand { get; set; } = string.Empty;
            public HashSet<ObjectId> AppendedIds { get; } = new HashSet<ObjectId>();
            public HashSet<ObjectId> ErasedIds { get; } = new HashSet<ObjectId>();
            public void Clear()
            {
                CurrentCommand = string.Empty;
                AppendedIds.Clear();
                ErasedIds.Clear();
            }
        }

        // Simple axis-aligned rectangle helper for extents overlap checks
        private class Rectangle
        {
            public Point3d Min { get; }
            public Point3d Max { get; }

            public Rectangle(Point3d min, Point3d max)
            {
                Min = min; Max = max;
            }

            public bool Overlaps(Rectangle other)
            {
                if (other == null) return false;
                if (Max.X < other.Min.X || other.Max.X < Min.X) return false;
                if (Max.Y < other.Min.Y || other.Max.Y < Min.Y) return false;
                return true;
            }
        }

        #endregion

        #region Deferred helper

        /// <summary>
        /// Defers an action to run once at the next Application.Idle, tracked per-document.
        /// If a previous deferred action exists for the same document it will be replaced.
        /// The action runs without acquiring the document lock — any action that modifies the DB should call LockDocument/transactions itself.
        /// </summary>
        private static void DeferActionForDocument(FoundationContext context, Action action)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;

            if (doc == null || action == null) return;

            // Remove existing handler for this doc if present
            if (_deferredIdleHandlers.TryRemove(doc, out var existing))
            {
                try { Application.Idle -= existing; } catch { }
            }

            void handler(object s, EventArgs e)
            {
                try
                {
                    // Unsubscribe immediately
                    Application.Idle -= handler;
                    _deferredIdleHandlers.TryRemove(doc, out _);

                    // Execute user action (may perform locking/transactions as needed)
                    action();
                }
                catch
                {
                    // swallow exceptions from deferred actions
                }
            }

            // Track and subscribe
            _deferredIdleHandlers.TryAdd(doc, handler);
            Application.Idle += handler;
        }

        #endregion

        public static bool TryGetBoundaryHandle(
            FoundationContext context,
            Transaction tr,
            out string handleString)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (tr == null) throw new ArgumentNullException(nameof(tr));

            handleString = null;

            var doc = context.Document;
            var db = doc.Database;

            // Open NOD
            var nod = (DBDictionary)
                tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            if (!nod.Contains(NODManager.ROOT))
                return false;

            var root = (DBDictionary)
                tr.GetObject(nod.GetAt(NODManager.ROOT), OpenMode.ForRead);

            if (!root.Contains(NODManager.KEY_BOUNDARY))
                return false;

            var boundaryDict = (DBDictionary)
                tr.GetObject(root.GetAt(NODManager.KEY_BOUNDARY), OpenMode.ForRead);

            // Boundary dictionary should contain exactly one entry
            foreach (DBDictionaryEntry entry in boundaryDict)
            {
                handleString = entry.Key;
                return true;
            }

            return false;
        }


        public bool TryRestoreBoundaryFromNOD(FoundationContext context, Database db, Transaction tr, out ObjectId boundaryId)
        {
            boundaryId = ObjectId.Null;

            // Get boundary handle from NOD
            if (!TryGetBoundaryHandle(context, tr, out string handleString))
                return false;

            if (!NODManager.TryParseHandle(context, handleString, out Handle handle))
                return false;

            if (!db.TryGetObjectId(handle, out ObjectId id))
                return false;

            if (!id.IsValid || id.IsErased)
                return false;

            boundaryId = id;  // return ObjectId
            return true;
        }

        internal void RestoreBoundaryAfterImport(FoundationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                if (TryRestoreBoundaryFromNOD(context, db, tr, out ObjectId boundaryId))
                {
                    // Set boundary in PolylineBoundaryManager (this triggers BoundaryChanged event)
                    if (!TrySetBoundary(context, boundaryId, out string error))
                    {
                        doc.Editor.WriteMessage($"\nFailed to set boundary: {error}");
                    }
                    else
                    {
                        doc.Editor.WriteMessage("\nBoundary restored from NOD.");
                    }
                }

                tr.Commit(); // read-only, but commit for consistency
            }
        }

        public bool SelectBoundary(FoundationContext context, out string error)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var doc = context.Document;

            error = null;

            if (doc == null) return false;
            var db = doc.Database;
            var ed = doc.Editor;


            // Prompt for a closed polyline
            PromptEntityOptions options = new PromptEntityOptions("\nSelect a closed polyline: ");
            options.SetRejectMessage("\nMust be a closed polyline.");
            options.AddAllowedClass(typeof(Polyline), false);

            var result = ed.GetEntity(options);
            if (result.Status != PromptStatus.OK) return false;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    // Open the selected entity
                    Polyline boundary = tr.GetObject(
                        result.ObjectId,
                        OpenMode.ForWrite) as Polyline;

                    if (boundary == null)
                    {
                        ed.WriteMessage("\nSelected object is not a polyline.");
                        return false;
                    }

                    // Try set the boundary (no DB writes assumed here)
                    if (!TrySetBoundary(context, result.ObjectId, out string boundaryError))
                    {
                        error = boundaryError;
                        ed.WriteMessage($"\nError setting boundary: {boundaryError}");
                        return false;
                    }

                    // Attach entity-side metadata
                    FoundationEntityData.Write(tr, boundary, NODManager.KEY_BOUNDARY);

                    // Register handle in the NOD
                    PolylineBoundaryManager.AddBoundaryHandleToNOD(context, tr, boundary.ObjectId);

                    tr.Commit();

                    ed.WriteMessage(
                        $"\nBoundary selected: {boundary.Handle}");
                    return true;
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nBoundary selection failed: {ex.Message}");
                    return false;
                }
            }
        }


    }
}
