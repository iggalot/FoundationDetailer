using FoundationDetailer.Model;
using Newtonsoft.Json;
using System;
using System.IO;

namespace FoundationDetailer.Storage
{
    public static class JsonStorage
    {
        // Default save location
        private static readonly string _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "FoundationDetailer_Model.json"
        );

        /// <summary>
        /// Save model to default path
        /// </summary>
        public static void SaveModel(FoundationModel model)
        {
            string json = JsonConvert.SerializeObject(model, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }

        /// <summary>
        /// Load model from default path
        /// </summary>
        public static FoundationModel LoadModel()
        {
            if (!File.Exists(_filePath))
                return null;

            string json = File.ReadAllText(_filePath);
            return JsonConvert.DeserializeObject<FoundationModel>(json);
        }

        /// <summary>
        /// Optional: clear saved model
        /// </summary>
        public static void ClearSavedModel()
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
    }
}
