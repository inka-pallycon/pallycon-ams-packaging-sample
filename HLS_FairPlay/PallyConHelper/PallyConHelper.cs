using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Azure.Management.Media.Models;
using PallyCon;

namespace PallyCon
{
    public class PallyConHelper
    {
        public static StreamingLocatorContentKey GetCbcsKeyFromPallyCon(string kms_url, string enc_token, string content_id, ref string hls_key_uri)
        {
            string key_id = "", key = "";
            PallyConKmsClientWrapper pallyconWrapper = new PallyConKmsClientWrapper(kms_url, enc_token); ;
            pallyconWrapper.getHlsPackagingInfoFromKmsServer(content_id, ref key_id, ref key, ref hls_key_uri);
            StreamingLocatorContentKey cbcsKey = new StreamingLocatorContentKey() { Id = Guid.Parse(key_id), Value = key };
            return cbcsKey;
        }
    }
}
