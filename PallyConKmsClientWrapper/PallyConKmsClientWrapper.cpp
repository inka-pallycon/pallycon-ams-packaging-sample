#include <string>
#include <msclr/marshal_cppstd.h>
#include <iostream>

#include "PallyConKmsClientWrapper.h"

namespace PallyCon {
	PallyConKmsClientWrapper::PallyConKmsClientWrapper(String^ strKmsURL, String^ strEncToken)
		: _kmsClient(new PallyConKmsClient(msclr::interop::marshal_as<std::string>(strKmsURL), msclr::interop::marshal_as<std::string>(strEncToken)))
	{
	}

	PallyConKmsClientWrapper::~PallyConKmsClientWrapper()
	{
		delete _kmsClient;
		_kmsClient = nullptr;
	}

	bool PallyConKmsClientWrapper::getDashPackagingInfoFromKmsServer(String^ content_id, String^% key_id, String^% key)
	{
		try
		{
			ContentPackagingInfo packInfos = _kmsClient->getContentPackagingInfoFromKmsServer(msclr::interop::marshal_as<std::string>(content_id), "", PackType::DASH);
			key_id = gcnew String(packInfos.keyId.c_str());
			key = gcnew String(packInfos.key.c_str());
		}
		catch (std::exception& e)
		{
			std::cout << e.what();
		}

		return true;
	}

	bool PallyConKmsClientWrapper::getHlsPackagingInfoFromKmsServer(String^ content_id, String^% key_id, String^% key, String^% hls_key_uri)
	{
		try
		{
			ContentPackagingInfo packInfos = _kmsClient->getContentPackagingInfoFromKmsServer(msclr::interop::marshal_as<std::string>(content_id), "", PackType::HLS);
			key_id = gcnew String(packInfos.keyId.c_str());
			key = gcnew String(packInfos.key.c_str());
			hls_key_uri = gcnew String(packInfos.hlsKeyUri.c_str());
		}
		catch (std::exception& e)
		{
			std::cout << e.what();
		}

		return true;
	}
}