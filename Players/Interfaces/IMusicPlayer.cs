using System;
using System.Threading.Tasks;
using MuSync.Models;
namespace MuSync.Players.Interfaces;
internal interface IMusicPlayer : IDisposable
{
    Task<PlayerInfo?> GetPlayerInfoAsync();
}