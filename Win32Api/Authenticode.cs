using System;
using System.Runtime.InteropServices;

namespace MuSync.Win32Api;

/// <summary>
/// Authenticode 数字签名校验（调用系统的 WinVerifyTrust）。
/// <para>
/// 为什么不用 <c>X509Certificate.CreateFromSignedFile</c>：它在 .NET 10 已标记过时（SYSLIB0057），
/// 而本项目开启了 <c>TreatWarningsAsErrors</c>。更重要的是，WinVerifyTrust 是系统权威实现，
/// 它同时检查签名摘要、证书链与 PE 的签名策略，比"能读出证书"严格得多。
/// </para>
/// <para>
/// 校验过程不访问网络（<see cref="WtdCacheOnlyUrlRetrieval"/>），否则吊销检查在断网时会长时间挂起，
/// 而这只是下载后的辅助校验，不值得为它阻塞 UI。
/// </para>
/// </summary>
internal static class Authenticode
{
    /// <summary>校验结果。</summary>
    public enum Result
    {
        /// <summary>有签名且可信（系统判定通过）。</summary>
        Valid,

        /// <summary>文件没有（内嵌的）Authenticode 签名。</summary>
        Unsigned,

        /// <summary>签名存在，但内容与签名不符或证书被明确不信任 —— 文件被人动过。</summary>
        Invalid,

        /// <summary>签名存在，但证书链无法验证（根不受信任、已过期、吊销状态无法确认等）。</summary>
        Unverified,

        /// <summary>调用失败或文件不可读，无法判断。</summary>
        Error
    }

    /// <summary>WinVerifyTrust 的操作 GUID：验证 PE 的 Authenticode 签名。</summary>
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    // WINTRUST_DATA.dwUIChoice
    private const uint WtdUiNone = 2;
    // WINTRUST_DATA.fdwRevocationChecks
    private const uint WtdRevokeNone = 0;
    // WINTRUST_DATA.dwUnionChoice
    private const uint WtdChoiceFile = 1;
    // WINTRUST_DATA.dwStateAction
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    // WINTRUST_DATA.dwProvFlags：只从本地缓存取吊销信息，避免联网等待
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;

    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    private const int TrustEBadDigest = unchecked((int)0x80096010);
    private const int TrustEExplicitDistrust = unchecked((int)0x800B0111);
    private const int CertERevoked = unchecked((int)0x800B010C);
    private const int CertEUntrustedRoot = unchecked((int)0x800B0109);
    private const int CertEChaining = unchecked((int)0x800B010A);
    private const int CertEExpired = unchecked((int)0x800B0101);
    private const int TrustESubjectNotTrusted = unchecked((int)0x800B0004);

    /// <summary>校验指定文件的 Authenticode 签名。</summary>
    /// <param name="filePath">待校验的可执行文件路径。</param>
    /// <param name="hresult">WinVerifyTrust 返回的原始状态码（便于日志定位）。</param>
    public static Result Verify(string filePath, out int hresult)
    {
        hresult = 0;
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            return Result.Error;
        }

        var fileInfoSize = Marshal.SizeOf<WintrustFileInfo>();
        var fileInfoPtr = Marshal.AllocHGlobal(fileInfoSize);
        try
        {
            var fileInfo = new WintrustFileInfo
            {
                Size = (uint)fileInfoSize,
                FilePath = filePath
            };
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var data = new WintrustData
            {
                Size = (uint)Marshal.SizeOf<WintrustData>(),
                UiChoice = WtdUiNone,
                RevocationChecks = WtdRevokeNone,
                UnionChoice = WtdChoiceFile,
                FileInfo = fileInfoPtr,
                StateAction = WtdStateActionVerify,
                ProviderFlags = WtdCacheOnlyUrlRetrieval
            };

            var action = ActionGenericVerifyV2;
            hresult = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            // 无论结果如何都要关闭状态句柄，否则会泄漏（WinVerifyTrust 的约定）
            data.StateAction = WtdStateActionClose;
            _ = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            return Classify(hresult);
        }
        catch (Exception)
        {
            return Result.Error;
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    /// <summary>
    /// 把 WinVerifyTrust 的状态码归类。
    /// <para>
    /// ⚠️ 这里刻意把"文件被动过"和"证书链验证不了"分开：
    /// 前者（摘要不符 / 被明确不信任 / 已吊销）说明内容有问题，必须拒绝；
    /// 后者（根不受信任 / 过期 / 吊销状态查不到）可能只是本机证书库或网络的问题，
    /// 一律当"篡改"会误杀正常更新包，所以只降级为"无法验证"。
    /// </para>
    /// </summary>
    private static Result Classify(int hresult) => hresult switch
    {
        0 => Result.Valid,

        // 没有内嵌签名：可能是真的没签，也可能只有目录签名（Windows 自带程序常这样）
        TrustENoSignature => Result.Unsigned,
        // 不是带签名的 PE（例如测试用的假文件）
        TrustESubjectFormUnknown => Result.Unsigned,

        // 内容与签名不符 / 明确不信任 / 已吊销 —— 文件被改过
        TrustEBadDigest => Result.Invalid,
        TrustEExplicitDistrust => Result.Invalid,
        CertERevoked => Result.Invalid,

        // 证书链问题：可能只是本机信任库或网络的问题
        CertEUntrustedRoot => Result.Unverified,
        CertEChaining => Result.Unverified,
        CertEExpired => Result.Unverified,
        TrustESubjectNotTrusted => Result.Unverified,

        _ => Result.Error
    };

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, ref WintrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustFileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
