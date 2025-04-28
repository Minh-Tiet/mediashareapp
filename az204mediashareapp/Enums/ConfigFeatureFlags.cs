using System.ComponentModel;

namespace MVCMediaShareAppNew.Enums
{
    public enum ConfigFeatureFlags
    {
        [Description("Enable Deletion any other blog for any other account")]
        AllowBlogDeletionAny,
        [Description("Enable storing SAS token with blob url on container level")]
        StoreBlobItemWithSas
    }
}