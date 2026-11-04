// Copyright (c) Files Community
// Licensed under the MIT License.

#include <iostream>
#include <algorithm>
#include <cwctype>
#include <string>
#include <dwmapi.h>
#include <exdisp.h>
#include <iostream>
#include <objbase.h>
#include <propvarutil.h>
#include <shtypes.h>
#include <ShlObj_core.h>
#include <ShObjIdl_core.h>
#include <tlhelp32.h>
#include <vector>
#include <wil/resource.h>

#include <winrt/base.h>
#include <winrt/Windows.ApplicationModel.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Management.Deployment.h>

#include "OpenInFolder.h"

// Link additional libraries
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "Propsys.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "uuid.lib")
#pragma comment(lib, "dwmapi.lib")

constexpr auto ID_TIMEREXPIRED = 101;

LRESULT CALLBACK WindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam);
bool OpenInExistingShellWindow(const TCHAR* folderPath);
bool IsLaunchedByExplorer();
void WaitForProtocolActivation(HANDLE hProcess);
void RunFileExplorer(const TCHAR* openDirectory);
std::wstring EnsurePortableIdentity(const std::wstring& root);
std::wstring GetIdentityStateValue(const wchar_t* name);
void SetIdentityStateValue(const wchar_t* name, const std::wstring& value);
size_t strifind(const std::wstring& strHaystack, const std::wstring& strNeedle);
bool comparei(std::wstring stringA, std::wstring stringB);
std::string wstring_to_utf8_hex(const std::wstring& input);
std::wstring str2wstr(const std::string& str);

std::wstring GetIdentityStateValue(const wchar_t* name)
{
	HKEY key = nullptr;
	if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\Files\\PortableIdentity", 0, KEY_READ, &key) != ERROR_SUCCESS)
		return {};

	WCHAR buffer[1024] = {};
	DWORD size = sizeof(buffer) - sizeof(WCHAR);
	DWORD type = 0;
	LSTATUS status = RegQueryValueExW(key, name, nullptr, &type, reinterpret_cast<LPBYTE>(buffer), &size);
	RegCloseKey(key);
	return status == ERROR_SUCCESS ? std::wstring(buffer) : std::wstring();
}

void LauncherLog(const wchar_t* message)
{
	WCHAR tempPath[MAX_PATH] = {};
	if (!GetTempPathW(_countof(tempPath), tempPath))
		return;

	std::wstring path = std::wstring(tempPath) + L"files-launcher.log";
	FILE* file = nullptr;
	if (_wfopen_s(&file, path.c_str(), L"a") != 0 || file == nullptr)
		return;

	SYSTEMTIME time = {};
	GetLocalTime(&time);
	fwprintf(file, L"%04u-%02u-%02u %02u:%02u:%02u %s\r\n", time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond, message);
	fclose(file);
}

void SetIdentityStateValue(const wchar_t* name, const std::wstring& value)
{
	HKEY key = nullptr;
	if (RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\Files\\PortableIdentity", 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr) != ERROR_SUCCESS)
		return;

	RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), static_cast<DWORD>((value.size() + 1) * sizeof(WCHAR)));
	RegCloseKey(key);
}

// Registers the optional identity package that ships beside the portable launcher and
// returns its AppUserModelId. On Windows builds where unpackaged WinUI 3 applications lose
// the IME candidate window, launching through the package identity restores it; when the
// package is missing or registration fails, the caller falls back to a plain CreateProcess.
// Registration is a repair step and never blocks startup.
std::wstring EnsurePortableIdentity(const std::wstring& root)
{
	// Diagnostics/QA switch: lets tests compare the build with and without package identity.
	WCHAR optOut[8] = {};
	if (GetEnvironmentVariableW(L"FILES_DISABLE_PORTABLE_IDENTITY", optOut, _countof(optOut)) > 0)
		return {};

	const std::wstring identityPackageName = L"Files.Internal.Portable.Identity";
	const std::wstring identityApplicationId = L"FilesPortable";
	const std::wstring identityPath = root + L"\\Files.Internal.Portable.Identity.msix";

	if (GetFileAttributesW(identityPath.c_str()) == INVALID_FILE_ATTRIBUTES)
	{
		LauncherLog(L"identity: package file missing beside the launcher");
		return {};
	}

	try
	{
		winrt::Windows::Management::Deployment::PackageManager manager;

		winrt::hstring installedFullName;
		winrt::hstring familyName;
		auto refresh = [&](winrt::Windows::Management::Deployment::PackageManager const& source)
		{
			for (auto const& package : source.FindPackagesForUser(L""))
			{
				if (_wcsicmp(package.Id().Name().c_str(), identityPackageName.c_str()) != 0)
					continue;

				installedFullName = package.Id().FullName();
				familyName = package.Id().FamilyName();
				break;
			}
		};

		refresh(manager);

		const bool registered = !installedFullName.empty();
		LauncherLog(registered ? L"identity: package already registered" : L"identity: package not registered");
		if (!registered || _wcsicmp(GetIdentityStateValue(L"Root").c_str(), root.c_str()) != 0)
		{
			if (registered)
				manager.RemovePackageAsync(installedFullName).get();

			winrt::Windows::Management::Deployment::AddPackageOptions options;
			options.ExternalLocationUri(winrt::Windows::Foundation::Uri(root));
			options.ForceUpdateFromAnyVersion(true);
			manager.AddPackageByUriAsync(winrt::Windows::Foundation::Uri(identityPath), options).get();
			LauncherLog(L"identity: registration completed");

			familyName = {};
			for (auto const& package : manager.FindPackagesForUser(L"", identityPackageName))
			{
				familyName = package.Id().FamilyName();
				break;
			}

			SetIdentityStateValue(L"Root", root);

			// The deployment stack publishes the registration asynchronously, and activating a
			// package before it is visible starts the app without a usable activation context
			// (AppInstance fails with 0x80040154). Poll briefly for the family name; a fresh
			// PackageManager is used because an existing instance caches its package list.
			for (int attempt = 0; attempt < 25 && familyName.empty(); ++attempt)
			{
				Sleep(200);
				winrt::Windows::Management::Deployment::PackageManager probe;
				refresh(probe);
			}
		}

		if (familyName.empty())
		{
			LauncherLog(L"identity: family name unavailable after registration");
			return {};
		}

		return std::wstring(familyName.c_str()) + L"!" + identityApplicationId;
	}
	catch (winrt::hresult_error const& ex)
	{
		WCHAR message[512] = {};
		swprintf_s(message, L"identity: hresult 0x%08X %s", static_cast<uint32_t>(ex.code()), ex.message().c_str());
		LauncherLog(message);
		return {};
	}
	catch (...)
	{
		LauncherLog(L"identity: unknown failure");
		return {};
	}
}


int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int cmdShow)
{
	auto oleCleanup = wil::OleInitialize_failfast();

	FILE* _debugStream = NULL;
	PWSTR pszPath = NULL;
	HRESULT hr = SHGetKnownFolderPath(FOLDERID_Desktop, 0, NULL, &pszPath);

	if (SUCCEEDED(hr))
	{
		TCHAR debugPath[MAX_PATH];
		swprintf(debugPath, _countof(debugPath) - 1, L"%s\\%s", pszPath, L"open_in_folder.txt");
		//_wfreopen_s(&_debugStream, debugPath, L"w", stdout);
		CoTaskMemFree(pszPath);
	}

	//Uncomment to attach debugger
	//Sleep(10 * 1000);

	int numArgs = 0;
	bool withArgs = false;
	LPWSTR* szArglist = CommandLineToArgvW(GetCommandLine(), &numArgs);
	WCHAR openDirectory[MAX_PATH];

	if (numArgs > 1 && wcsnlen(szArglist[1], 1))
	{
		swprintf(openDirectory, _countof(openDirectory) - 1, L"%s", szArglist[1]);
		std::wcout << openDirectory << std::endl;
		withArgs = true;
	}

	LocalFree(szArglist);

	// Portable layout: this launcher sits at the root and the application binaries live in lib\.
	WCHAR modulePath[MAX_PATH] = {};
	if (GetModuleFileNameW(nullptr, modulePath, _countof(modulePath)) > 0)
	{
		if (WCHAR* lastSeparator = wcsrchr(modulePath, L'\\'))
		{
			*lastSeparator = L'\0';

			WCHAR appPath[MAX_PATH] = {};
			swprintf(appPath, _countof(appPath) - 1, L"%s\\lib\\Files.exe", modulePath);

			if (_waccess(appPath, 0) == 0)
			{
				std::wstring identityAumid = EnsurePortableIdentity(modulePath);

				SetEnvironmentVariableW(L"FILES_PORTABLE_ROOT", modulePath);

				// Keep every argument the shell passed, including quoted paths.
				std::wstring originalCommandLine = GetCommandLineW();
				const wchar_t* forwardedArguments = originalCommandLine.c_str();
				if (*forwardedArguments == L'"')
				{
					forwardedArguments++;
					while (*forwardedArguments && *forwardedArguments != L'"')
						forwardedArguments++;
					if (*forwardedArguments)
						forwardedArguments++;
				}
				else
				{
					while (*forwardedArguments && !iswspace(*forwardedArguments))
						forwardedArguments++;
				}

				std::wstring childCommandLine = L"\"" + std::wstring(appPath) + L"\"" + forwardedArguments;

				// Prefer package activation when the identity package is available: the child then
				// starts with package identity, which the IME candidate UI needs on affected builds.
				if (!identityAumid.empty())
				{
					IApplicationActivationManager* activationManager = nullptr;
					if (SUCCEEDED(CoCreateInstance(CLSID_ApplicationActivationManager, nullptr, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&activationManager))))
					{
						DWORD activatedProcessId = 0;
						HRESULT activationResult = activationManager->ActivateApplication(
							identityAumid.c_str(),
							*forwardedArguments ? forwardedArguments : nullptr,
							AO_NONE,
							&activatedProcessId);
						activationManager->Release();

						if (SUCCEEDED(activationResult))
							return 0;
					}
				}

				STARTUPINFOW startupInfo = { sizeof(startupInfo) };
				PROCESS_INFORMATION processInfo = {};
				if (CreateProcessW(appPath, childCommandLine.data(), nullptr, nullptr, FALSE, 0, nullptr, modulePath, &startupInfo, &processInfo))
				{
					CloseHandle(processInfo.hThread);
					CloseHandle(processInfo.hProcess);
					return 0;
				}

				MessageBoxW(
					nullptr,
					L"Files could not be started. Re-extract the portable package and try again.",
					L"Files - Internal",
					MB_OK | MB_ICONERROR
				);

				return 1;
			}
		}
	}

	WCHAR szBuf[MAX_PATH];
	ExpandEnvironmentStringsW(L"%LOCALAPPDATA%\\Microsoft\\WindowsApps\\files-internal.exe", szBuf, MAX_PATH - 1);
	std::wcout << szBuf << std::endl;
	if (_waccess(szBuf, 0) == -1)
	{
		std::cout << "Files has been uninstalled" << std::endl;

		MessageBox(
			NULL,
			(LPCWSTR)L"Files - Internal has been uninstalled. Opening File Explorer. Existing registry values are left unchanged.",
			(LPCWSTR)L"Files - Internal",
			(UINT)(MB_OK)
		);

		// Run explorer
		RunFileExplorer(withArgs ? openDirectory : NULL);

		if (_debugStream)
			fclose(_debugStream);

		return 0;
	}

	if (withArgs)
	{
		if (IsLaunchedByExplorer() && OpenInExistingShellWindow(openDirectory))
		{
			if (_debugStream)
				fclose(_debugStream);

			return 0;
		}

		// Register the window class.
		const wchar_t CLASS_NAME[] = L"Files Window Class";

		WNDCLASSEX wcex = { };
		wcex.cbSize = sizeof(wcex);
		wcex.lpfnWndProc = WindowProc;
		wcex.cbWndExtra = sizeof(OpenInFolder*);
		wcex.hInstance = hInstance;
		wcex.lpszClassName = CLASS_NAME;
		RegisterClassEx(&wcex);

		winrt::com_ptr<OpenInFolder> openInFolder;
		openInFolder.attach(new OpenInFolder());

		// Create the window.
		HWND hwnd = CreateWindowEx(
			WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
			CLASS_NAME,
			L"Files Launcher",
			0,
			0, 0, 0, 0,
			NULL,
			NULL,
			hInstance,
			openInFolder.get()
		);

		if (hwnd == NULL)
		{
			if (_debugStream)
				fclose(_debugStream);

			return 0;
		}

		SetTimer(hwnd, ID_TIMEREXPIRED, 500, NULL);

		MSG msg = { };
		while (GetMessage(&msg, NULL, 0, 0) > 0)
		{
			TranslateMessage(&msg);
			DispatchMessage(&msg);
		}

		auto item = openInFolder->GetResult();
		if (IsWindow(hwnd))
			KillTimer(hwnd, ID_TIMEREXPIRED);

		auto launchFiles = [&](const wchar_t* verb, const std::wstring& target, bool waitForActivation) -> bool
		{
			TCHAR args[1024];
			swprintf(args, _countof(args) - 1, L"\"%s\" %s \"%s\"", szBuf, verb, target.c_str());

			std::wstring uriWithArgs = L"files-internal:?cmd=" + str2wstr(wstring_to_utf8_hex(args));

			std::wcout << L"Invoking: " << args << L" = " << uriWithArgs << std::endl;

			SHELLEXECUTEINFO ShExecInfo = { 0 };
			ShExecInfo.cbSize = sizeof(SHELLEXECUTEINFO);
			ShExecInfo.fMask = SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI | SEE_MASK_NOCLOSEPROCESS;
			ShExecInfo.lpFile = uriWithArgs.c_str();
			ShExecInfo.lpDirectory = openDirectory;
			ShExecInfo.nShow = SW_SHOW;

			if (!ShellExecuteEx(&ShExecInfo))
			{
				std::wcout << L"Protocol error: " << GetLastError() << std::endl;
				return false;
			}

			if (waitForActivation)
				WaitForProtocolActivation(ShExecInfo.hProcess);
			else if (ShExecInfo.hProcess)
				CloseHandle(ShExecInfo.hProcess);

			return true;
		};

		if (!item.empty())
		{
			openInFolder->RevokeShellWindow();
			if (IsWindow(hwnd))
				DestroyWindow(hwnd);

			std::wcout << L"Item: " << item << std::endl;
			launchFiles(L"-select", item, true);
		}
		else if (OpenInExistingShellWindow(openDirectory))
		{
			openInFolder->RevokeShellWindow();
			if (IsWindow(hwnd))
				DestroyWindow(hwnd);

			if (_debugStream)
				fclose(_debugStream);

			return 0;
		}
		else
		{
			// Open the folder right away, but keep the shell window registered and the
			// message pump running: a SHOpenFolderAndSelectItems caller that is still
			// probing IShellWindows can deliver its selection after the first timeout,
			// in which case it is forwarded with a follow-up -select activation.
			// Pumping here also keeps the process alive while the asynchronous
			// -directory protocol activation is delivered (#18818).
			std::wcout << L"No item selected" << std::endl;

			if (launchFiles(L"-directory", openDirectory, false) && IsWindow(hwnd))
			{
				SetTimer(hwnd, ID_TIMEREXPIRED, 10000, NULL);

				MSG graceMsg = { };
				while (GetMessage(&graceMsg, NULL, 0, 0) > 0)
				{
					TranslateMessage(&graceMsg);
					DispatchMessage(&graceMsg);
				}

				item = openInFolder->GetResult();
			}

			openInFolder->RevokeShellWindow();
			if (IsWindow(hwnd))
				DestroyWindow(hwnd);

			if (!item.empty())
			{
				std::wcout << L"Item: " << item << std::endl;
				launchFiles(L"-select", item, true);
			}
		}
	}
	else
	{
		std::wcout << L"Invoking: no arguments" << std::endl;

		SHELLEXECUTEINFO ShExecInfo = { 0 };
		ShExecInfo.cbSize = sizeof(SHELLEXECUTEINFO);
		ShExecInfo.fMask = SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI | SEE_MASK_NOCLOSEPROCESS;
		ShExecInfo.lpFile = L"files-internal:";
		ShExecInfo.nShow = SW_SHOW;

		if (!ShellExecuteEx(&ShExecInfo))
		{
			std::wcout << L"Protocol error: " << GetLastError() << std::endl;
			//ShExecInfo.lpFile = L"files-internal.exe";
			//if (!ShellExecuteEx(&ShExecInfo))
			//{
				//std::wcout << L"Command line error: " << GetLastError() << std::endl;
			//}
		}
		else
		{
			WaitForProtocolActivation(ShExecInfo.hProcess);
		}
	}

	if (_debugStream)
		fclose(_debugStream);

	return 0;
}

LRESULT CALLBACK WindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
{
	auto* pContainer = (OpenInFolder*)GetWindowLongPtr(hwnd, GWLP_USERDATA);

	switch (uMsg)
	{
	case WM_NCCREATE:
	{
		CREATESTRUCT* pCreate = reinterpret_cast<CREATESTRUCT*>(lParam);
		pContainer = reinterpret_cast<OpenInFolder*>(pCreate->lpCreateParams);

		if (!pContainer)
		{
			PostQuitMessage(0);
			return 0;
		}

		pContainer->SetWindow(hwnd);
		SetWindowLongPtr(hwnd, GWLP_USERDATA, (LONG_PTR)pContainer);

		BOOL cloak = TRUE;
		DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, &cloak, sizeof(cloak));
		break;
	}

	case WM_NCDESTROY:
		SetWindowLongPtr(hwnd, GWLP_USERDATA, 0);
		return 0;

	case WM_TIMER:
		switch (wParam)
		{
		case ID_TIMEREXPIRED:
			PostQuitMessage(0);
			return 0;
		}
		break;
	}

	 // Jump across to the member window function (will handle all requests).
	if (pContainer != nullptr)
		return pContainer->WindowProcedure(hwnd, uMsg, wParam, lParam);
	else
		return DefWindowProc(hwnd, uMsg, wParam, lParam);
}

size_t strifind(const std::wstring& strHaystack, const std::wstring& strNeedle)
{
	auto it = std::search(
		strHaystack.begin(), strHaystack.end(),
		strNeedle.begin(), strNeedle.end(),
		[](wchar_t ch1, wchar_t ch2) { return std::toupper(ch1) == std::toupper(ch2); }
	);

	return it != strHaystack.end() ? it - strHaystack.begin() : std::wstring::npos;
}

bool comparei(std::wstring stringA, std::wstring stringB)
{
	auto toUpperW = [](wchar_t c) { return static_cast<wchar_t>(std::toupper(c)); };
	transform(stringA.begin(), stringA.end(), stringA.begin(), toUpperW);
	transform(stringB.begin(), stringB.end(), stringB.begin(), toUpperW);

	return (stringA == stringB);
}

std::string wstring_to_utf8_hex(const std::wstring& input)
{
	std::string output;

	int cbNeeded = WideCharToMultiByte(CP_UTF8, 0, input.c_str(), -1, NULL, 0, NULL, NULL);
	if (cbNeeded > 0)
	{
		char* utf8 = new char[cbNeeded];
		if (WideCharToMultiByte(CP_UTF8, 0, input.c_str(), -1, utf8, cbNeeded, NULL, NULL) != 0)
		{
			for (char* p = utf8; *p; p++)
			{
				char onehex[5];
				sprintf_s(onehex, sizeof(onehex), "%%%02.2X", (unsigned char)*p);
				output.append(onehex);
			}
		}

		delete[] utf8;
	}

	return output;
}

std::wstring str2wstr(const std::string& str)
{
	int cbNeeded = MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(), NULL, 0);
	if (cbNeeded > 0)
	{
		std::wstring wstrTo(cbNeeded, 0);
		MultiByteToWideChar(CP_UTF8, 0, &str[0], (int)str.size(), &wstrTo[0], cbNeeded);
		return wstrTo;
	}

	return L"";
}

// Packaged-app protocol activations are delivered asynchronously even with
// SEE_MASK_NOASYNC; exiting before delivery completes discards the activation
// and Files never opens (#18818). Wait on the activated process when the shell
// provides one, otherwise give the activation broker a grace period.
void WaitForProtocolActivation(HANDLE hProcess)
{
	if (hProcess)
	{
		WaitForInputIdle(hProcess, 5000);
		CloseHandle(hProcess);
	}
	else
	{
		Sleep(2000);
	}
}

void RunFileExplorer(const TCHAR* openDirectory)
{
	// Run explorer
	SHELLEXECUTEINFO ShExecInfo = { 0 };
	ShExecInfo.cbSize = sizeof(SHELLEXECUTEINFO);
	ShExecInfo.lpFile = L"explorer.exe";

	if (openDirectory != NULL)
	{
		TCHAR args[1024];
		swprintf(args, _countof(args) - 1, L"\"%s\"", openDirectory);
		ShExecInfo.lpParameters = args;
	}

	ShExecInfo.nShow = SW_SHOW;
	ShellExecuteEx(&ShExecInfo);
}

bool IsLaunchedByExplorer()
{
	DWORD explorerProcessId = 0;
	GetWindowThreadProcessId(GetShellWindow(), &explorerProcessId);
	if (!explorerProcessId)
		return false;

	wil::unique_handle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0));
	if (!snapshot)
		return false;

	PROCESSENTRY32 processEntry{ sizeof(processEntry) };
	if (!Process32First(snapshot.get(), &processEntry))
		return false;

	do
	{
		if (processEntry.th32ProcessID == GetCurrentProcessId())
			return processEntry.th32ParentProcessID == explorerProcessId;
	} while (Process32Next(snapshot.get(), &processEntry));

	return false;
}

bool OpenInExistingShellWindow(const TCHAR* folderPath)
{
	std::wstring openDirectory(folderPath);
	bool mustOpenInExplorer = false;
	constexpr auto godModeClsid = L"{ED7BA470-8E54-465E-825C-99712043E01C}";

	if (strifind(openDirectory, L"::{") == 0)
		openDirectory = L"shell:" + openDirectory;

	// Exclude this shell address so that it opens in File Explorer
	if (strifind(openDirectory, godModeClsid) != std::wstring::npos)
		mustOpenInExplorer = true;

	if (strifind(openDirectory, L"shell:") == 0)
	{
		std::vector<std::wstring> supportedShellFolders{
			L"shell:::{645FF040-5081-101B-9F08-00AA002F954E}",
			L"shell:::{5E5F29CE-E0A8-49D3-AF32-7A7BDC173478}",
			L"shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
			L"shell:::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}",
			L"shell:::{208D2C60-3AEA-1069-A2D7-08002B30309D}",
			L"Shell:RecycleBinFolder", L"Shell:NetworkPlacesFolder", L"Shell:MyComputerFolder"
		};

		auto it = std::find_if(
			supportedShellFolders.begin(), supportedShellFolders.end(),
			[openDirectory](std::wstring it) { return comparei(it, openDirectory); }
		);

		mustOpenInExplorer = it == supportedShellFolders.end();
	}

	IShellItem* psi;
	PIDLIST_ABSOLUTE controlPanelCategoryViewPidl;

	if (!SUCCEEDED(SHCreateItemFromParsingName(L"::{26EE0668-A00A-44D7-9371-BEB064C98683}", NULL, IID_IShellItem, (void**)&psi)))
	{
		if (mustOpenInExplorer)
			RunFileExplorer(openDirectory.c_str());

		return mustOpenInExplorer;
	}

	if (!SUCCEEDED(SHGetIDListFromObject(psi, &controlPanelCategoryViewPidl)))
	{
		psi->Release();
		if (mustOpenInExplorer)
			RunFileExplorer(openDirectory.c_str());

		return mustOpenInExplorer;
	}

	psi->Release();

	PIDLIST_ABSOLUTE targetFolderPidl;

	if (!SUCCEEDED(SHCreateItemFromParsingName(openDirectory.c_str(), NULL, IID_IShellItem, (void**)&psi)))
	{
		if (mustOpenInExplorer)
			RunFileExplorer(openDirectory.c_str());

		return mustOpenInExplorer;
	}

	if (!SUCCEEDED(SHGetIDListFromObject(psi, &targetFolderPidl)))
	{
		psi->Release();
		if (mustOpenInExplorer)
			RunFileExplorer(openDirectory.c_str());

		return mustOpenInExplorer;
	}

	psi->Release();

	bool opened = false;
	IShellWindows* shellWindows;
	if (SUCCEEDED(CoCreateInstance(CLSID_ShellWindows, NULL, CLSCTX_LOCAL_SERVER, IID_IShellWindows, (void**)&shellWindows)))
	{
		VARIANT v;
		V_VT(&v) = VT_I4;
		IDispatch* item;
		for (V_I4(&v) = 0; SUCCEEDED(shellWindows->Item(v, &item)) && item != NULL; V_I4(&v)++)
		{
			IServiceProvider* serv;
			if (SUCCEEDED(item->QueryInterface(IID_IServiceProvider, (void**)&serv)))
			{
				IShellBrowser* shellBrowser;
				if (SUCCEEDED(serv->QueryService(SID_STopLevelBrowser, IID_IShellBrowser, (void**)&shellBrowser)))
				{
					IShellView* shellView;
					if (SUCCEEDED(shellBrowser->QueryActiveShellView(&shellView)))
					{
						IFolderView* folderView;
						if (SUCCEEDED(shellView->QueryInterface(IID_IFolderView, (void**)&folderView)))
						{
							IPersistFolder2* folder;
							if (SUCCEEDED(folderView->GetFolder(IID_IPersistFolder2, (void**)&folder)))
							{
								PIDLIST_ABSOLUTE folderPidl;
								if (SUCCEEDED(folder->GetCurFolder(&folderPidl)))
								{
									if (ILIsParent(folderPidl, targetFolderPidl, true) ||
										ILIsEqual(folderPidl, controlPanelCategoryViewPidl))
									{
										if (SUCCEEDED(shellBrowser->BrowseObject(targetFolderPidl, SBSP_SAMEBROWSER | SBSP_ABSOLUTE)))
										{
											opened = true;
											break;
										}
									}
									CoTaskMemFree(folderPidl);
								}
								folder->Release();
							}
							folderView->Release();
						}
						shellView->Release();
					}
					shellBrowser->Release();
				}
				serv->Release();
			}
			item->Release();
		}

		shellWindows->Release();
	}

	CoTaskMemFree(targetFolderPidl);
	CoTaskMemFree(controlPanelCategoryViewPidl);

	if (!opened && mustOpenInExplorer)
		RunFileExplorer(openDirectory.c_str());

	return opened || mustOpenInExplorer;
}
