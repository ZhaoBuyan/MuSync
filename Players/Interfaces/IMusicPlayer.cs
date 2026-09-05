using System.Threading.Tasks;
using MuSync.Models;
namespace MuSync.Players.Interfaces;
internal interface IMusicPlayer
{
    Task<PlayerInfo?> GetPlayerInfoAsync();
}