using System;
using MuSync.Win32Api;

namespace MuSync.Utils;

/// <summary>更新包的签名状态（给策略判断用，避免业务代码直接碰 Win32 常量）。</summary>
internal enum BinarySignatureStatus
{
    /// <summary>有签名且系统判定可信。</summary>
    Valid,

    /// <summary>没有内嵌数字签名。</summary>
    Unsigned,

    /// <summary>签名存在但内容与签名不符、或证书被明确不信任 / 已吊销 —— 文件被人动过。</summary>
    Invalid,

    /// <summary>签名存在但证书链验证不了（根不受信任、过期、吊销状态未知）。不当作篡改。</summary>
    Unverified,

    /// <summary>无法判断（文件不可读等）。</summary>
    Unknown
}

/// <summary>
/// 更新包签名策略。
/// <para>
/// 分两档处理，原因是两者性质不同：
/// </para>
/// <list type="bullet">
/// <item>**签名无效**：文件被人动过或证书不受信任 —— 必须拒绝，不能提示"你自己判断"。</item>
/// <item>**没有签名**：本项目的发行包目前确实没有代码签名证书，这属于既知现状 —— 不能因此把更新流程整个废掉，
/// 但必须在界面上如实告知，并提醒哈希校验的局限。</item>
/// </list>
/// <para>
/// 关于局限：SHA-256 摘要来自 GitHub API，与更新包走**同一条 TLS 连接**。若这条连接被伪造成立
/// （例如 CA 被攻破），攻击者可以同时伪造摘要和文件，"校验通过"说明不了来源。真正能锚定来源的是
/// 代码签名，所以一旦将来给发行包签了名，这里会自动从"警告"升级为"可信"。
/// </para>
/// </summary>
internal static class BinarySignature
{
    /// <summary>校验文件签名。不访问网络，不会阻塞。</summary>
    public static BinarySignatureStatus Check(string? filePath, out int hresult)
    {
        switch (Authenticode.Verify(filePath ?? "", out hresult))
        {
            case Authenticode.Result.Valid:
                return BinarySignatureStatus.Valid;
            case Authenticode.Result.Unsigned:
                return BinarySignatureStatus.Unsigned;
            case Authenticode.Result.Invalid:
                return BinarySignatureStatus.Invalid;
            case Authenticode.Result.Unverified:
                return BinarySignatureStatus.Unverified;
            default:
                return BinarySignatureStatus.Unknown;
        }
    }

    /// <summary>是否该按"被人动过"处理（只有这一档才拒绝使用）。</summary>
    public static bool MustReject(BinarySignatureStatus status) => status == BinarySignatureStatus.Invalid;

    /// <summary>
    /// 给用户看的、非阻断的提示：没有签名 / 签名验证不了 / 读不出签名。
    /// 返回空串表示没有要提醒的（签名有效）。
    /// </summary>
    public static string WarningFor(BinarySignatureStatus status) => status switch
    {
        BinarySignatureStatus.Unsigned => UnsignedWarning(),
        BinarySignatureStatus.Unverified => Loc.L(
            "提示：这个更新包有数字签名，但本机无法验证它的证书链（根证书不受信任、证书过期，或吊销状态查不到）。这不等于文件被篡改，但也无法确认来源。",
            "Note: this update package is digitally signed, but its certificate chain could not be validated here (untrusted root, expired certificate, or revocation status unavailable). That does not mean the file was tampered with, but it also does not confirm its origin."),
        BinarySignatureStatus.Unknown => Loc.L(
            "提示：无法读取这个更新包的签名信息，来源无法确认。",
            "Note: the signature info of this update package could not be read, so its origin cannot be confirmed."),
        _ => ""
    };

    /// <summary>签名无效时给用户看的说明（用于拒绝使用该更新包）。</summary>
    public static string InvalidMessage() => Loc.L(
        "更新包的数字签名校验没通过：文件可能被篡改，或签名证书不受信任。已拒绝使用，请从 GitHub Release 页面重新下载。",
        "The update package's digital signature is invalid: the file may have been tampered with, or its signing certificate is untrusted. It has been rejected — please download it again from the GitHub release page.");

    /// <summary>没有签名时给用户看的提示（不阻断，但必须说明局限）。</summary>
    public static string UnsignedWarning() => Loc.L(
        "提示：这个更新包没有数字签名，无法从密码学上证明它来自本项目。上面的 SHA-256 校验只能排除传输损坏，排除不了伪造——请确认它是从 GitHub Release 页面下载的。",
        "Note: this update package is not digitally signed, so its origin cannot be proven cryptographically. The SHA-256 check above rules out transfer corruption, not forgery — make sure you downloaded it from the GitHub release page.");
}
