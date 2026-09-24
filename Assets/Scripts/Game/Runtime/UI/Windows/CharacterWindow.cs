using Cysharp.Threading.Tasks;
using RPG.Game;
using RPG.Game.UI.Controllers;
using RPG.Game.UI.Escape;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.UIModule;

namespace WS_Modules.UIModule
{
    /// <summary>统一角色展示窗口；窗口生命周期委托给 CharacterWindowController。</summary>
    [InfoBox("依赖同一根节点上的 CharacterWindowController 和 CharacterWindowDataComponent。")]
    public sealed class CharacterWindow : WindowBase
    {
        #region 依赖字段

        private CharacterWindowController controller;
        private EscCommandRegistration escCommandRegistration;
        private IArchitecture escapeArchitecture;

        #endregion

        #region 生命周期

        /// <summary>初始化角色窗口序列化依赖和 Controller。</summary>
        public override void OnAwake()
        {
            CharacterWindowDataComponent data = GameObject.GetComponent<CharacterWindowDataComponent>();
            if (data == null) throw new System.InvalidOperationException("[CharacterWindow] 根节点缺少 CharacterWindowDataComponent。");
            data.ValidateConfiguration();
            FullScreenWindow = data.IsFullWindow;
            SetDoAnimation(data.DoAnimation);
            base.OnAwake();
            controller = GameObject.GetComponent<CharacterWindowController>();
            if (controller == null) throw new System.InvalidOperationException("[CharacterWindow] 根节点缺少 CharacterWindowController。");
            controller.Initialize(data);
        }

        /// <summary>显示窗口并注册当前 Esc 关闭命令。</summary>
        public override void OnShow()
        {
            base.OnShow();
            UnregisterEscCommand();
            escapeArchitecture = GameArchitecture.Interface;
            escCommandRegistration = escapeArchitecture.SendCommand(
                new RegisterEscCommand(new CloseCharacterWindowCommand()));
            controller.OnWindowShown();
        }

        /// <summary>隐藏窗口后清理显示数据与图集释放计时。</summary>
        public override void OnHide()
        {
            UnregisterEscCommand();
            controller.OnWindowHidden();
            base.OnHide();
        }

        /// <summary>销毁窗口并对称释放 Controller。</summary>
        public override void OnDestroy()
        {
            UnregisterEscCommand();
            controller?.Dispose();
            base.OnDestroy();
        }

        #endregion

        #region API

        /// <summary>在窗口隐藏时预热角色相关动态图集。</summary>
        public void PrepareOpen()
        {
            controller.PrepareOpen();
        }

        /// <summary>等待本轮动态图集准备完成。</summary>
        /// <returns>动态图集加载任务。</returns>
        public UniTask PrepareOpenAsync()
        {
            return controller.PrepareOpenAsync();
        }

        #endregion

        #region 内部辅助

        /// <summary>注销角色窗口显示期间的 Esc 命令。</summary>
        private void UnregisterEscCommand()
        {
            if (!escCommandRegistration.IsValid) return;
            escapeArchitecture?.SendCommand(new UnregisterEscCommand(escCommandRegistration));
            escCommandRegistration = default;
        }

        #endregion
    }
}
