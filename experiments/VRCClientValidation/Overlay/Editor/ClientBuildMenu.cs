using System;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3A.Editor;

namespace VLat.HeadFrameExperiment
{
    internal static class ClientBuildMenu
    {
        // SDK window/account handling remains a human action. This helper never opens or authenticates it.
        [MenuItem("VLat/Client Validation/Build and Test selected avatar (local only)")]
        private static async void BuildSelected()
        {
            try
            {
                if (!System.IO.File.Exists("client-experiment-inputs.json"))throw new InvalidOperationException("隔離client project専用です。");
                GameObject target=Selection.activeGameObject;
                if(target==null || target.GetComponent<VRCAvatarDescriptor>()==null)
                    throw new InvalidOperationException("scene上の診断Avatarルートを選択してください。");
                if(!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var builder))
                    throw new InvalidOperationException("SDKパネルを手動で開いてBuilderを準備してください。自動認証は行いません。");
                await builder.BuildAndTest(target);
                Debug.Log("ローカルBuild & Test完了。クライアント検証V1〜V10は別途必要です。");
            }
            catch(Exception error) {Debug.LogException(error);}
        }
    }
}
