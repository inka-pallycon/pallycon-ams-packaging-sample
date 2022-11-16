---------------------------------------
# PallyConAMSv3DotnetSample
This sample shows how to integrate PallyCon Multi DRM with AMS(Azure Media Services) v3 using [.NET 6.0 SDK](https://dotnet.microsoft.com/en-us/download). Since this sample focused on DRM integration, only the simple VOD packaging scenario of AMS is used here, see the [AMS SDK link](https://github.com/Azure-Samples/media-services-v3-dotnet) for more information on AMS features.
> There are two scenario can be run in this sample.
> 1. DASH_PlayreadyAndWidevine
> 2. HLS_FairPlay
---------------------------------------
## Prerequisites
- A Windows 10/11 PC
- Visual Studio 2022 (Windows 10/11)
- .NET 6.0 SDK : https://dotnet.microsoft.com/download
- Azure Media Services account : https://learn.microsoft.com/en-us/azure/media-services/latest/account-create-how-to?tabs=portal
- KMS token used for CPIX API communication with PallyCon KMS. This is an API authentication token that is generated when you sign up PallyCon service, and can be found on the PallyCon Console site.
---------------------------------------
## How to launch and test the project
1. Clone or download this sample repository.
2. Open the root /PallyConAMSv3DotnetSample.sln and select the active project to launch in Visual Studio.
3. Set values including PallyConEncToken(KMS Token) in appsettings.json. Please refer [this link](https://learn.microsoft.com/en-us/azure/media-services/latest/configure-connect-dotnet-howto#set-values-in-appsettingsjson) if needed.
4. Set SourceUri and ContentId in the source code.
5. Run the project.
---------------------------------------
## PallyConKMSClientWrapper
C++/CLI project for wrap a C++ library(*PallyConKmsClient_MD.lib*) to communicate with PallyCon KMS server.
The _getContentPackagingInfoFromKmsServer_ function allows you to obtain packaging information from the KMS server.

<pre><code>
bool PallyConKmsClientWrapper::getDashAndHlsPackagingInfoFromKmsServer(String^ content_id, String^% key_id, String^% key
		, String^% hls_key_uri, String^% iv, String^% pssh_widevine, String^% pssh_playready)
{
	try
	{
		PallyConKmsClient kmsClient = new PallyConKmsClient(
				msclr::interop::marshal_as<std::string>(strKmsURL), msclr::interop::marshal_as<std::string>(strEncToken))
        
		ContentPackagingInfo packInfos = kmsClient->getContentPackagingInfoFromKmsServer(
				msclr::interop::marshal_as<std::string>(content_id), "", PackType::DASH|PackType::HLS);
		key_id = gcnew String(packInfos.keyId.c_str());
		key = gcnew String(packInfos.key.c_str());
		hls_key_uri = gcnew String(packInfos.hlsKeyUri.c_str());
		iv = gcnew String(packInfos.iv.c_str());
		pssh_widevine = gcnew String(packInfos.pssh_widevine.c_str());
		pssh_playready = gcnew String(packInfos.pssh_playready.c_str());
	}
	catch (std::exception& e)
	{
		std::cout << e.what();
	}

  return true;
}
</pre></code>

---------------------------------------

## References
- https://pallycon.com/docs/en/multidrm/
- https://pallycon.com/docs/en/multidrm/packaging/cpix-api/
- https://learn.microsoft.com/ko-kr/azure/media-services/
---------------------------------------
