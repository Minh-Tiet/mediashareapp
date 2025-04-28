using System;
using System.Text.Json.Serialization;

namespace MVCMediaShareAppNew.Models.ViewModels
{
    public class UserImage
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("userName")]
        public string UserName { get; set; }

        [JsonPropertyName("originMediaName")]
        public string OriginMediaName { get; set; }

        [JsonPropertyName("imageBlogName")]
        public string ImageBlogName { get; set; }

        [JsonPropertyName("imageUrlWithSas")]
        public string ImageUrlWithSas { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
    }
} 