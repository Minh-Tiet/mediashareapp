namespace MVCMediaShareAppNew.Models.SettingsModels
{
    public class CosmosDbSettings
    {
        public string AccountEndpoint { get; set; }
        public string AccountName { get; set; }
        public string AccountKey { get; set; }
        public string DatabaseName { get; set; }
        public string ContainerName { get; set; }
        public string MediaStoreContainerName { get; set; }
    }
} 