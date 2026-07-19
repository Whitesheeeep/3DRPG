using UnityEditor;
using UnityEngine;

namespace WS_Modules
{
    interface IRename<T>
    {
        void Begin();
        bool TryRename(T target, string newName, out string error);
        void End();
    }

    sealed class AssetDatabaseRenamer : IRename<AssetInfo>
    {
        public void Begin()
        {
            AssetDatabase.StartAssetEditing();
        }

        public bool TryRename(AssetInfo target, string newName, out string error)
        {
            error = AssetDatabase.RenameAsset(target.Path, newName);
            return string.IsNullOrEmpty(error);
        }

        public void End()
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }

    sealed class GameObjectRenamer : IRename<GameObject>
    {
        private const string UndoOperationName = "Batch Rename GameObjects";

        public void Begin()
        {
        }

        public bool TryRename(GameObject target, string newName, out string error)
        {
            if (target == null)
            {
                error = "Missing GameObject reference.";
                return false;
            }

            Undo.RecordObject(target, UndoOperationName);
            target.name = newName;
            EditorUtility.SetDirty(target);
            error = string.Empty;
            return true;
        }

        public void End()
        {
        }
    }
}