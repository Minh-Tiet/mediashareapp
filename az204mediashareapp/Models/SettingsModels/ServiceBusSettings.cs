namespace MVCMediaShareAppNew.Models.SettingsModels
{
    public class ServiceBusSettings
    {
        public string ConnectionString { get; set; }
        public string DefaultQueueName { get; set; }
        public string ResizeQueueName { get; set; }
        public string WatermarkQueueName { get; set; }
        public string ArchiveQueueName { get; set; }
    }
}
