using System;
using System.IO;
using Newtonsoft.Json;

namespace FoundationDetailer.Storage
{
    public static class JsonStorage
    {
        /// <summary>
        /// Saves the model to a JSON file.
        /// </summary>
        public static void Save<T>(string filePath, T model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model), "Cannot save a null model.");

            try
            {
                string json = JsonConvert.SerializeObject(model, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error saving model to JSON: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads a model from a JSON file.
        /// </summary>
        public static T Load<T>(string filePath) where T : class
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                string json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading model from JSON: {ex.Message}", ex);
            }
        }
    }
}
