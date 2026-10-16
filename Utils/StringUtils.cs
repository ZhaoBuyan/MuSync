using System;
using System.Text;
namespace MuSync.Utils;
/// <summary>字符串工具：UTF-8 截断边界处理（不切断多字节字符）。</summary>
public static class StringUtils
{
    /// <summary>
    /// 找出 buffer 里最后一个**完整字符**的结束位置（返回的是"可以用作拷贝长度"的下标 + 1）。
    /// <para>
    /// ⚠️ 早先返回的是该字符的**起始**下标，而调用方拿它当拷贝长度用，等于把那个完整字符也丢掉了：
    /// 标称 7 字节上限时 "hello中" 会变成 "hell..."（"o" 与 "中" 一起丢）。
    /// 现在 ASCII 分支返回 position + 1、多字节分支返回 position + required + 1。
    /// </para>
    /// </summary>
    private static int FindLastCompleteCharIndex(byte[] buffer, int byteCount)
    {
        for (var position = byteCount - 1; position >= 0; position--)
        {
            if (buffer[position] < 0x80)
                return position + 1;
            if ((buffer[position] & 0xC0) != 0x80) continue;
            var count = 0;
            while (position >= 0 && (buffer[position] & 0xC0) == 0x80)
            {
                position--;
                count++;
            }
            if (position < 0) continue;
            var lead = buffer[position];
            int required;
            switch (lead >> 4)
            {
                case 0b1100 or 0b1101: required = 1; break; 
                case 0b1110: required = 2; break; 
                case 0b1111: required = 3; break; 
                default: continue; 
            }
            if (count == required)
                return position + required + 1;
        }
        return -1;
    }
    public static string GetTruncatedStringByMaxByteLength(string str, int maxLength)
    {
        var strBuffer = Encoding.UTF8.GetBytes(str);
        // 恰好等于上限时无需截断（原先用 < 会让它白白多出一个省略号）
        if (strBuffer.Length <= maxLength) return str;
        var truncatedLength = maxLength - 3;
        var truncatedBuffer = new byte[truncatedLength];
        Buffer.BlockCopy(strBuffer, 0, truncatedBuffer, 0, truncatedLength);
        var lastCompleteCharIndex = FindLastCompleteCharIndex(truncatedBuffer, truncatedLength);
        if (lastCompleteCharIndex == -1) return "Error";
        var truncatedString = new byte[lastCompleteCharIndex];
        Buffer.BlockCopy(truncatedBuffer, 0, truncatedString, 0, lastCompleteCharIndex);
        var ellipsisByteString = "..."u8.ToArray();
        var finalBuffer = new byte[truncatedString.Length + ellipsisByteString.Length];
        Buffer.BlockCopy(truncatedString, 0, finalBuffer, 0, truncatedString.Length);
        Buffer.BlockCopy(ellipsisByteString, 0, finalBuffer, truncatedString.Length, ellipsisByteString.Length);
        return Encoding.UTF8.GetString(finalBuffer);
    }
}