using System;
using System.Reflection;
using Candide.Multiplayer.Network;
using CandideServer.Server;

namespace RomesteadShared;

// Version-proof shim for the two game networking calls whose signatures changed between Romestead builds.
// The 0.25.2_1 public-test branch added `reliable`/`ordered` parameters:
//   NetworkManager.SendMessage(string, object, byte)            -> (string, object, byte, bool, bool)
//   BaseServer.SendHostMessageToAll(string, object)             -> (string, object, bool, bool)
// A direct call compiles against whichever DLLs are installed and HARD-BINDS to that one signature, so it
// throws MissingMethodException on the other version (this is exactly what spammed the logs). Routing every
// send through here resolves the method by NAME at runtime and fills any extra trailing optional params with
// their defaults (Type.Missing) — so ONE build runs on both the current stable game and 0.25.2_1.
//
// Vendored copy of the author's shared co-op shim, so this repo builds on its own. Reflection cost is negligible:
// sends are batched/throttled, not per-frame-per-call.
public static class Net
{
    private static MethodInfo _send;        // NetworkManager.SendMessage(string, object, byte, ...)
    private static int _sendCount;
    private static MethodInfo _hostAll;     // BaseServer.SendHostMessageToAll(string, object, ...)
    private static int _hostAllCount;
    private static bool _resolved;

    private static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;
        try
        {
            foreach (var m in typeof(NetworkManager).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "SendMessage") continue;
                var p = m.GetParameters();
                if (p.Length >= 3 && p[0].ParameterType == typeof(string)
                    && p[1].ParameterType == typeof(object) && p[2].ParameterType == typeof(byte))
                { _send = m; _sendCount = p.Length; break; }
            }
            foreach (var m in typeof(BaseServer).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "SendHostMessageToAll") continue;
                var p = m.GetParameters();
                if (p.Length >= 2 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(object))
                { _hostAll = m; _hostAllCount = p.Length; break; }
            }
        }
        catch { /* leave nulls -> calls below no-op */ }
    }

    // Client -> server. `type` keeps the value every caller used (2). Extra params on the newer signature
    // (reliable/ordered) take their compiler defaults (true/false) via Type.Missing.
    public static void SendMessage(string key, object message, byte type = 2)
    {
        Resolve();
        if (_send == null) return;
        var args = new object[_sendCount];
        args[0] = key; args[1] = message; args[2] = type;
        for (int i = 3; i < _sendCount; i++) args[i] = Type.Missing;
        _send.Invoke(null, BindingFlags.OptionalParamBinding | BindingFlags.InvokeMethod, null, args, null);
    }

    // Host -> all clients.
    public static void SendHostMessageToAll(string key, object message)
    {
        Resolve();
        var server = BaseServer.Instance;
        if (_hostAll == null || server == null) return;
        var args = new object[_hostAllCount];
        args[0] = key; args[1] = message;
        for (int i = 2; i < _hostAllCount; i++) args[i] = Type.Missing;
        _hostAll.Invoke(server, BindingFlags.OptionalParamBinding | BindingFlags.InvokeMethod, null, args, null);
    }
}
