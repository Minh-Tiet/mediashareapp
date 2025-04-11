using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageProcessor.Models
{
    public class MediaStoreItem
    {
        public string id { get; set; }
        public string MediaStorageBlobUrl { get; set; }
        public string MediaType { get; set; }
        public string AuthorId { get; set; }
    }
}
