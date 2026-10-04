namespace RPG.CameraSystem
{
    /// <summary>标识 Gameplay Camera 当前由玩家观察输入还是锁定构图规则驱动。</summary>
    public enum E_GameplayCameraMode
    {
        /// <summary>玩家可通过 Look 输入自由旋转镜头。</summary>
        FreeLook,
        /// <summary>镜头自动跟踪玩家与锁定目标并调整战斗构图。</summary>
        Locked
    }
}
