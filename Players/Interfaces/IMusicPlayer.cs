using System;
using System.Threading.Tasks;
using MuSync.Models;
namespace MuSync.Players.Interfaces;
/// <summary>音乐播放器读取接口：各播放器实现统一返回当前播放信息，无播放时返回 null。</summary>
internal interface IMusicPlayer : IDisposable
{
    Task<PlayerInfo?> GetPlayerInfoAsync();
}