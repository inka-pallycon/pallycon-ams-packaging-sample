using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Azure.Management.Media.Models;
using PallyCon;

namespace PallyCon
{
    public class PallyConHelper
    {
        public static StreamingLocatorContentKey GetCencKeyFromPallyCon(string kms_url, string enc_token, string content_id)
        {
            string key_id = "", key = "";
            PallyConKmsClientWrapper pallyconWrapper = new PallyConKmsClientWrapper(kms_url, enc_token); ;
            pallyconWrapper.getDashPackagingInfoFromKmsServer(content_id, ref key_id, ref key);
            StreamingLocatorContentKey cencKey = new StreamingLocatorContentKey() { Id = Guid.Parse(key_id), Value = key };
            return cencKey;
        }
    }
}
