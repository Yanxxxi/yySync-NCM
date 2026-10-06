using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MusicRpc;

public static class NativeEntry
{
    private static readonly Backend Instance = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static nint Dispatch(nint request)
    {
        string response;
        try
        {
            using var json = JsonDocument.Parse(Marshal.PtrToStringUTF8(request) ?? "{}");
            response = JsonSerializer.Serialize(Instance.Dispatch(json.RootElement), JsonOptions);
        }
        catch (Exception ex)
        {
            response = JsonSerializer.Serialize(new { ok = false, error = ex.Message }, JsonOptions);
        }
        return Marshal.StringToCoTaskMemUTF8(response);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static void Free(nint response) => Marshal.FreeCoTaskMem(response);
}
