#define WIN32_LEAN_AND_MEAN
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#endif
#ifndef _WCHAR_T_DEFINED
#define _WCHAR_T_DEFINED
#endif
#include <windows.h>
#include <nethost.h>
#include <hostfxr.h>
#include <coreclr_delegates.h>
#include <cstring>
#include <stdexcept>
#include <string>

// ABI shared with BetterNCM v2. Native string arguments are direct UTF-8 pointers.
enum class NativeAPIType : int { Int, Boolean, Double, String, V8Value };
using NativeFunction = char* (__cdecl*)(void**);
struct PluginAPI {
    int (__cdecl* addNativeAPI)(const NativeAPIType*, int, const char*, NativeFunction);
    const char* betterncmVersion;
    int processType;
    const unsigned short (*ncmVersion)[3];
};

static SRWLOCK gate = SRWLOCK_INIT;
struct Lock {
    Lock() { AcquireSRWLockExclusive(&gate); }
    ~Lock() { ReleaseSRWLockExclusive(&gate); }
};
template <typename T> static T Function(HMODULE module, const char* name) {
    return reinterpret_cast<T>(reinterpret_cast<void*>(GetProcAddress(module, name)));
}
static HMODULE hostModule;
static HMODULE netModule;
static void* (__cdecl* managedDispatch)(const char*);
static void (__cdecl* managedFree)(void*);
// The runtime remains loaded until its host process exits. Avoid C++ TLS
// destructors racing the CLR's shutdown threads during DLL detach.
static DWORD resultKey = TlsAlloc();
static std::wstring& hostError = *new std::wstring();
struct ResultBuffer { char* data; size_t capacity; };
static const char* Return(const std::string& text) {
    auto buffer = static_cast<ResultBuffer*>(TlsGetValue(resultKey));
    if (!buffer) {
        buffer = static_cast<ResultBuffer*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(ResultBuffer)));
        if (!buffer || !TlsSetValue(resultKey, buffer)) return "{\"ok\":false,\"error\":\"Allocation failed\"}";
    }
    if (buffer->capacity < text.size() + 1) {
        auto data = static_cast<char*>(HeapAlloc(GetProcessHeap(), 0, text.size() + 1));
        if (!data) return "{\"ok\":false,\"error\":\"Allocation failed\"}";
        if (buffer->data) HeapFree(GetProcessHeap(), 0, buffer->data);
        buffer->data = data;
        buffer->capacity = text.size() + 1;
    }
    memcpy(buffer->data, text.c_str(), text.size() + 1);
    return buffer->data;
}

static std::string Utf8(const std::wstring& input) {
    if (input.empty()) return {};
    int length = WideCharToMultiByte(CP_UTF8, 0, input.data(), (int)input.size(), nullptr, 0, nullptr, nullptr);
    std::string output(length, 0);
    WideCharToMultiByte(CP_UTF8, 0, input.data(), (int)input.size(), output.data(), length, nullptr, nullptr);
    return output;
}

static std::string Escape(const std::string& input) {
    std::string output;
    for (unsigned char value : input) {
        if (value == '"' || value == '\\') { output += '\\'; output += value; }
        else if (value < 32) output += ' ';
        else output += value;
    }
    return output;
}

static void HOSTFXR_CALLTYPE HostError(const char_t* text) {
    if (text) { hostError += text; hostError += L" "; }
}

static void LoadManaged() {
    if (managedDispatch) return;
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&LoadManaged), &module)) throw std::runtime_error("Cannot locate yySync backend DLL");
    std::wstring root(32768, 0);
    auto count = GetModuleFileNameW(module, root.data(), (DWORD)root.size());
    if (!count || count >= root.size()) throw std::runtime_error("Cannot read yySync backend path");
    root.resize(count);
    root.resize(root.find_last_of(L"\\/") + 1);
    auto assembly = root + L"managed\\yySync.Managed.dll";
    auto config = root + L"managed\\yySync.Managed.runtimeconfig.json";
    auto runtimeRoot = root + L"runtime";
    netModule = LoadLibraryExW((root + L"nethost.dll").c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!netModule) throw std::runtime_error("Cannot load bundled nethost.dll");
    auto getHostPath = Function<decltype(&get_hostfxr_path)>(netModule, "get_hostfxr_path");
    if (!getHostPath) throw std::runtime_error("Invalid nethost.dll");
    wchar_t hostPath[32768];
    size_t hostLength = 32768;
    bool bundledRuntime = GetFileAttributesW(runtimeRoot.c_str()) != INVALID_FILE_ATTRIBUTES;
    get_hostfxr_parameters parameters{sizeof(parameters), assembly.c_str(), bundledRuntime ? runtimeRoot.c_str() : nullptr};
    if (getHostPath(hostPath, &hostLength, &parameters) != 0)
        throw std::runtime_error("Cannot locate .NET runtime; reinstall the complete yySync plugin package");
    hostModule = LoadLibraryExW(hostPath, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!hostModule) throw std::runtime_error("Cannot load .NET hostfxr");
    auto init = Function<hostfxr_initialize_for_runtime_config_fn>(hostModule, "hostfxr_initialize_for_runtime_config");
    auto getDelegate = Function<hostfxr_get_runtime_delegate_fn>(hostModule, "hostfxr_get_runtime_delegate");
    auto close = Function<hostfxr_close_fn>(hostModule, "hostfxr_close");
    auto setError = Function<hostfxr_set_error_writer_fn>(hostModule, "hostfxr_set_error_writer");
    if (!init || !getDelegate || !close || !setError) throw std::runtime_error("Unsupported .NET hosting API");
    hostError.clear();
    auto previousWriter = setError(HostError);
    hostfxr_handle context = nullptr;
    hostfxr_initialize_parameters initializeParameters{sizeof(initializeParameters), nullptr,
        bundledRuntime ? runtimeRoot.c_str() : nullptr};
    auto rc = init(config.c_str(), &initializeParameters, &context);
    if (rc < 0 || !context) {
        setError(previousWriter);
        if (context) close(context);
        throw std::runtime_error("Unable to initialize .NET 9 x64 Runtime: " + Utf8(hostError));
    }
    load_assembly_and_get_function_pointer_fn load = nullptr;
    rc = getDelegate(context, hdt_load_assembly_and_get_function_pointer, reinterpret_cast<void**>(&load));
    close(context);
    setError(previousWriter);
    if (rc < 0 || !load) throw std::runtime_error("Cannot initialize managed component loader");
    void* dispatchPointer = nullptr;
    void* freePointer = nullptr;
    rc = load(assembly.c_str(), L"MusicRpc.NativeEntry, yySync.Managed", L"Dispatch",
        UNMANAGEDCALLERSONLY_METHOD, nullptr, &dispatchPointer);
    if (rc < 0 || !dispatchPointer) throw std::runtime_error("Cannot load yySync.Managed Dispatch entry");
    rc = load(assembly.c_str(), L"MusicRpc.NativeEntry, yySync.Managed", L"Free",
        UNMANAGEDCALLERSONLY_METHOD, nullptr, &freePointer);
    if (rc < 0 || !freePointer) throw std::runtime_error("Cannot load yySync.Managed Free entry");
    managedDispatch = reinterpret_cast<decltype(managedDispatch)>(dispatchPointer);
    managedFree = reinterpret_cast<decltype(managedFree)>(freePointer);
}

extern "C" __declspec(dllexport) const char* __cdecl yySyncDispatch(const char* request) {
    Lock lock;
    try {
        LoadManaged(); // Deliberately outside DllMain / Windows loader lock.
        auto response = managedDispatch(request ? request : "{}");
        std::string text = response ? static_cast<const char*>(response) : "{}";
        managedFree(response);
        return Return(text);
    } catch (const std::exception& error) {
        return Return("{\"ok\":false,\"error\":\"" + Escape(error.what()) + "\"}");
    } catch (...) {
        return Return("{\"ok\":false,\"error\":\"Native backend failure\"}");
    }
}

static char* __cdecl Dispatch(void** args) {
    return const_cast<char*>(yySyncDispatch(args ? static_cast<const char*>(args[0]) : "{}"));
}

extern "C" __declspec(dllexport) int __cdecl BetterNCMPluginMain(PluginAPI* api) {
    if (!api || !api->addNativeAPI) return -1;
    if (api->processType != 0x10) return 0; // Only the renderer hosts JavaScript.
    HMODULE pinned;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&BetterNCMPluginMain), &pinned);
    static const NativeAPIType arguments[] = {NativeAPIType::String};
    return api->addNativeAPI(arguments, 1, "yysync.dispatch", Dispatch);
}
