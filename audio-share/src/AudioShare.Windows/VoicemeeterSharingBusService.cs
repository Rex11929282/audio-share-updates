using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AudioShare.Windows;

public sealed class VoicemeeterSharingBusService
{
    private const string RemoteDll = @"C:\Program Files (x86)\VB\Voicemeeter\VoicemeeterRemote64.dll";

    [DllImport(RemoteDll, CharSet = CharSet.Ansi)]
    private static extern int VBVMR_Login();

    [DllImport(RemoteDll)]
    private static extern int VBVMR_Logout();

    [DllImport(RemoteDll, CharSet = CharSet.Ansi)]
    private static extern int VBVMR_SetParameters(string script);

    [DllImport(RemoteDll, CharSet = CharSet.Ansi)]
    private static extern int VBVMR_GetParameterFloat(string parameter, out float value);

    [DllImport(RemoteDll)]
    private static extern int VBVMR_GetLevel(int type, int channel, out float value);

    [DllImport(RemoteDll)]
    private static extern int VBVMR_IsParametersDirty();

    public void SetMainInputShared(bool shared)
    {
        var loginResult = VBVMR_Login();
        if (loginResult < 0)
        {
            throw new InvalidOperationException("无法连接 Voicemeeter Banana。");
        }

        try
        {
            // AUX is the local-only bus; it must never reach Discord's B1 output.
            var result = VBVMR_SetParameters($"Strip[3].B1={(shared ? 1 : 0)};Strip[4].B1=0;");
            if (result != 0)
            {
                throw new InvalidOperationException("无法更新 Voicemeeter B1 输出。");
            }

            var target = shared ? 1f : 0f;
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                // Refresh Voicemeeter's parameter cache before reading the B1 switches.
                VBVMR_IsParametersDirty();
                var inputResult = VBVMR_GetParameterFloat("Strip[3].B1", out var inputB1);
                var auxResult = VBVMR_GetParameterFloat("Strip[4].B1", out var auxB1);
                if (inputResult == 0 && auxResult == 0 &&
                    Math.Abs(inputB1 - target) < 0.01f && Math.Abs(auxB1) < 0.01f)
                {
                    return;
                }

                Thread.Sleep(100);
            }

            throw new InvalidOperationException("Voicemeeter 未在 5 秒内确认 B1 状态。");
        }
        finally
        {
            VBVMR_Logout();
        }
    }

    public SharingBusStatus GetStatus()
    {
        var loginResult = VBVMR_Login();
        if (loginResult < 0)
        {
            throw new InvalidOperationException("Unable to connect to Voicemeeter Banana.");
        }

        try
        {
            VBVMR_IsParametersDirty();
            var inputSwitchResult = VBVMR_GetParameterFloat("Strip[3].B1", out var inputB1);
            var auxSwitchResult = VBVMR_GetParameterFloat("Strip[4].B1", out var auxB1);
            var inputLeftResult = VBVMR_GetLevel(0, 6, out var inputLeft);
            var inputRightResult = VBVMR_GetLevel(0, 7, out var inputRight);
            if (inputSwitchResult != 0 || auxSwitchResult != 0 || inputLeftResult != 0 || inputRightResult != 0)
            {
                throw new InvalidOperationException("Unable to read Voicemeeter B1 or Input status.");
            }

            return new SharingBusStatus(
                inputB1 >= 0.99f,
                auxB1 >= 0.99f,
                Math.Max(inputLeft, inputRight));
        }
        finally
        {
            VBVMR_Logout();
        }
    }
}

public sealed record SharingBusStatus(bool IsMainInputShared, bool IsAuxShared, float InputLevel);
