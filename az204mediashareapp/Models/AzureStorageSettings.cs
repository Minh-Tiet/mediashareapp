namespace MVCMediaShareAppNew.Models
{
    public class AzureStorageSettings
    {
        public string ConnectionString { get; set; }
        public string AccountName { get; set; }
        public string AccountKey { get; set; }
        public string ContainerName { get; set; }
        public string QueueName { get; set; }
    }
} 