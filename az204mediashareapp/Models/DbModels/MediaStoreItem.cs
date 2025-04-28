using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MVCMediaShareAppNew.Models.DbModels
{
    public class MediaStoreItem
    {
        public string id { get; set; }
        public string OriginMediaName { get; set; }
        public string MediaStorageBlobName { get; set; }
        public string MediaStorageBlobUrl { get; set; }
        public string MediaType { get; set; }
        public string AuthorId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
