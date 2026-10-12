using System.Threading;
using Cysharp.Threading.Tasks;

namespace RPG.Character
{
    /// <summary>定义队伍成员全部死亡后的恢复策略。</summary>
    public interface ICharacterPartyDefeatHandler
    {
        /// <summary>异步处理团灭；默认策略加载庭院并复活队伍成员。</summary>
        /// <param name="manager">持有队伍 Actor 与恢复入口的 CharacterManager。</param>
        /// <param name="cancellationToken">Player 生命周期取消令牌。</param>
        /// <returns>团灭恢复流程。</returns>
        UniTask HandlePartyDefeatedAsync(CharacterManager manager, CancellationToken cancellationToken);
    }
}
