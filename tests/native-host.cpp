#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <iostream>
#include <string>

using Function = char* (__cdecl*)(void**);
struct Api {
    int (__cdecl* add)(const int*, int, const char*, Function);
    const char* version;
    int process;
    void* ncm;
};
static Function dispatch;
static int registrations;
static int __cdecl Add(const int* types, int count, const char* name, Function function) {
    if (count != 1 || types[0] != 3 || std::string(name) != "yysync.dispatch") return -1;
    dispatch = function;
    registrations++;
    return 1; // BetterNCM's implementation returns true, unlike its older wiki.
}
int main(int argc, char** argv) {
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
    if (argc != 2) return 2;
    auto dll = LoadLibraryExA(argv[1], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!dll) { std::cerr << "DLL load failed: " << GetLastError(); return 3; }
    auto entry = reinterpret_cast<int(__cdecl*)(Api*)>(reinterpret_cast<void*>(GetProcAddress(dll, "BetterNCMPluginMain")));
    if (!entry) return 4;
    Api api{Add, "1.3.4", 1, nullptr};
    if (entry(&api) != 0 || registrations != 0) return 5;
    api.process = 0x11; // Renderer flag may be combined with other flags.
    if (entry(&api) != 0 || registrations != 1 || !dispatch) return 6;
    std::string request;
    while (std::getline(std::cin, request)) {
        void* arguments[] = {const_cast<char*>(request.c_str())};
        const char* result = dispatch(arguments);
        std::cout << (result ? result : "{}") << std::endl;
    }
    return 0;
}
