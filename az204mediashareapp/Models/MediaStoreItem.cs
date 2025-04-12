using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MVCMediaShareAppNew.Models
{
    public class MediaStoreItem
    {
        public string id { get; set; }
        public string OriginMediaName { get; set; }
        public string MediaStorageBlobName { get; set; }
        public string MediaStorageBlobUrlWithSas { get; set; }
        public string MediaType { get; set; }
        public string AuthorId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
