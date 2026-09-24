namespace RPG.Character
{
    /// <summary>集中保存新建角色配置使用的默认头像图集 Address。</summary>
    public static class CharacterAssetAddresses
    {
        /// <summary>侧面头像默认图集 Address；角色配置仍可在 Editor 中手动修改。</summary>
        public const string SideIconsAtlas = "CharacterSideIcons";
        /// <summary>角色头像默认图集 Address；角色配置仍可在 Editor 中手动修改。</summary>
        public const string AvatarAtlas = "Characters";
        /// <summary>角色全身立绘默认图集 Address；未导入对应 Sprite 时窗口保持透明。</summary>
        public const string FullBodyPortraitsAtlas = "CharacterFullBody";
    }
}
