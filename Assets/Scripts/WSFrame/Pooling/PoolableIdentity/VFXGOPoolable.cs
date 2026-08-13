using WS_Modules.Pooling;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 为项目自有 VFX 包装 Prefab 提供统一的对象池身份，并复用基础 Transform 与激活状态生命周期。
    /// </summary>
    public sealed class VFXGOPoolable : PoolObjectIdentity
    {
    }
}
