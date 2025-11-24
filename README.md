

AutoCAD 2023 plugin (C# .NET) for foundation detailing: boundary selection, rebar and strand generation, piers, grade beams, curb/brick ledge support, transient preview, and project persistence (DWG XRecords + JSON).


## Project structure (in this document)
- FoundationDetailer.sln (not included; create via Visual Studio)
- FoundationDetailer/ FoundationDetailer.csproj
- FoundationDetailer/Commands/FoundationCommands.cs
- FoundationDetailer/Data/FoundationModel.cs
- FoundationDetailer/Data/BoundaryPolygon.cs
- FoundationDetailer/Data/Pier.cs
- FoundationDetailer/Data/GradeBeam.cs
- FoundationDetailer/Data/RebarBar.cs
- FoundationDetailer/Data/Strand.cs
- FoundationDetailer/Data/CompanyStandards.cs
- FoundationDetailer/AutoCAD/AutoCADAdapter.cs
- FoundationDetailer/Storage/JsonStorage.cs
- FoundationDetailer/Preview/PreviewManager.cs
- FoundationDetailer/UI/PaletteMain.xaml
- FoundationDetailer/UI/PaletteMain.xaml.cs
- FoundationDetailer/Properties/AssemblyInfo.cs


## How to build
1. Create a new Class Library (.NET Framework 4.8) project targeting the AutoCAD 2023 .NET API.
2. Add references to:
- AcDbMgd.dll
- AcMgd.dll
- AcCoreMgd.dll (as needed)
- PresentationFramework, PresentationCore, WindowsBase (for WPF)
3. Copy the files from this document into your project.
4. Set `Copy Local = false` for AutoCAD dll references.
5. Build and load the compiled DLL into AutoCAD using NETLOAD.


## Usage
- Run command `FOUNDATIONDETAIL` to open the palette.
- Select a closed polyline boundary in AutoCAD when prompted.
- Use the palette to configure rebar, piers, grade beams, slopes, drops, curbs.
- Click "Preview" to see transient graphics in the drawing.
- Click "Apply" to write geometry to the drawing and embed XRecords.
- The project auto-saves JSON next to the DWG and writes summary XRecord at the drawing root.


## Notes
- This is a starter scaffold. Geometry algorithms (bar layout, sloped surfaces, form-filling rules) are implemented in a basic way; tune for your company's standards.
- Extensive unit tests are recommended for geometry generation modules.