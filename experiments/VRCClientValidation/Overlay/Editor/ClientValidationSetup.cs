using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace VLat.HeadFrameExperiment
{
    /// <summary>隔離projectで実機診断Avatarを生成し、Unity側の参照と描画だけを検査します。</summary>
    [InitializeOnLoad]
    public static class ClientValidationSetup
    {
        private const string Active = "VLat.ClientPrep.Active";
        private static string Output => Path.GetFullPath("results-client-preflight");
        static ClientValidationSetup()
        {
            if (SessionState.GetBool(Active, false)) EditorApplication.delayCall += Resume;
        }

        /// <summary>新規22f1 stagingでAvatar、scene、参照検査結果を生成します。実機PASSは生成しません。</summary>
        public static void RunBatch()
        {
            if (!Application.isBatchMode || !File.Exists("client-experiment-inputs.json") || Application.unityVersion != "2022.3.22f1")
                throw new InvalidOperationException("新規22f1 client staging専用です。");
            if (Directory.Exists(Output)) throw new InvalidOperationException("結果の上書きを拒否します。");
            Directory.CreateDirectory(Output);SessionState.SetBool(Active,true);EditorApplication.delayCall += Resume;
        }

        private static void Resume()
        {
            if (!SessionState.GetBool(Active,false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Resume;return; }
            SessionState.SetBool(Active,false);
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                GameObject root=ClientAvatarAssets.Create();
                var checks=new Dictionary<string,object>();
                checks["evidence_source"]="UnityEditorPreflight";checks["unity"]=Application.unityVersion;
                checks["gpu"]=SystemInfo.graphicsDeviceName;checks["graphics_api"]=SystemInfo.graphicsDeviceType.ToString();
                checks["sdk"]="3.10.5";checks["utc"]=DateTime.UtcNow.ToString("O");
                checks["humanoid_valid"]=root.GetComponent<Animator>().avatar.isValid && root.GetComponent<Animator>().isHuman;
                checks["missing_scripts"]=MissingScripts(root);
                var components=new List<string>();
                foreach(Component c in root.GetComponentsInChildren<Component>(true))
                {
                    if(c==null) throw new InvalidOperationException("Missing script");
                    string name=c.GetType().FullName;components.Add(name);
                    if(!name.StartsWith("UnityEngine.") && !name.StartsWith("VRC.")) throw new InvalidOperationException("任意runtime component: "+name);
                }
                checks["component_types"]=components;
                var shaderMessages=new Dictionary<string,object>();
                foreach(string name in new[] {"VLat/Experiments/ClientDiagnostic","VLat/Experiments/ClientNoStereoArrays","VLat/Experiments/ClientSolid"})
                {
                    Shader shader=Shader.Find(name);if(shader==null || !shader.isSupported) throw new InvalidOperationException("Shaderなし/非対応: "+name);
                    shaderMessages[name]=ShaderUtil.GetShaderMessages(shader);
                    if(ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Shader error: "+name);
                }
                checks["shader_messages"]=shaderMessages;
                foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if(r.sharedMesh==null || r.rootBone==null || r.sharedMaterial==null || r.sharedMaterial.shader==null)
                        throw new InvalidOperationException("Renderer参照欠落: "+r.name);
                checks["renderer_count"]=root.GetComponentsInChildren<SkinnedMeshRenderer>().Length;
                checks["blueprint_blank"]=string.IsNullOrEmpty(root.GetComponent<VRC.Core.PipelineManager>().blueprintId);
                GameObject cameraObject=new GameObject("PreviewCamera");Camera camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
                camera.transform.position=new Vector3(0,1.35f,2.5f);camera.transform.LookAt(new Vector3(0,1.25f,0));
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.08f,.08f,1);camera.nearClipPlane=.01f;camera.farClipPlane=20;
                SaveImage(camera,768,768,"avatar-preview.png");
                // Full-scene card framing preserves actual skinning and shader object binding. Other renderers are hidden only for capture.
                foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>())r.enabled=false;
                for(int card=0;card<3;++card)
                {
                    var r=root.transform.Find("Card"+card).GetComponent<SkinnedMeshRenderer>();r.enabled=true;
                    Bounds b=r.sharedMesh.bounds;float distance=2;
                    camera.transform.SetPositionAndRotation(b.center+Vector3.forward*distance,Quaternion.Euler(0,180,0));
                    camera.fieldOfView=2*Mathf.Atan(b.size.y/(2*distance))*Mathf.Rad2Deg;
                    SaveImage(camera,272,512,"card"+card+".png");r.enabled=false;
                }
                var head=root.transform.Find("HeadProbe").GetComponent<SkinnedMeshRenderer>();head.enabled=true;
                camera.transform.position=new Vector3(.6f,1.8f,1.5f);camera.transform.LookAt(new Vector3(0,1.67f,0));camera.fieldOfView=30;
                byte[] actual=SaveImage(camera,256,256,"effect-off.png");head.enabled=false;
                // Compare two separately initialized prefabs, rather than changing rootBone between synchronous Camera.Render calls.
                GameObject reference=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ClientAvatarAssets.ReferencePrefab));
                foreach(var r in reference.GetComponentsInChildren<SkinnedMeshRenderer>())r.enabled=r.name=="HeadProbe";
                byte[] baseline=SaveImage(camera,256,256,"source-reference.png");UnityEngine.Object.DestroyImmediate(reference);
                int differences=0;for(int i=0;i<actual.Length;++i) if(actual[i]!=baseline[i])++differences;
                checks["off_reference_different_channels"]=differences;
                checks["off_reference_pixels"]=256*256;
                foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>())r.enabled=true;
                camera.transform.position=new Vector3(0,1.35f,2.5f);camera.transform.LookAt(new Vector3(0,1.25f,0));camera.fieldOfView=60;
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ClientAvatarAssets.Folder+"/ClientValidation.unity");
                checks["sdk_build_and_test"]="NOT RUN: SDK panel/account initialization left to human; no authentication invoked";
                checks["client_V1_V10"]="NOT RUN";
                // Player shader compilation smoke test. This is explicitly NOT the SDK avatar build/validation.
                string bundleFolder=Path.GetFullPath("client-player-smoke");Directory.CreateDirectory(bundleFolder);
                var bundle=BuildPipeline.BuildAssetBundles(bundleFolder,new[] {new AssetBundleBuild {assetBundleName="vlat-client-preflight",assetNames=new[] {ClientAvatarAssets.Prefab,ClientAvatarAssets.ReferencePrefab}}},BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.StandaloneWindows64);
                checks["unity_player_assetbundle_built"]=bundle!=null;
                File.WriteAllText(Path.Combine(Output,"preflight.json"),JsonConvert.SerializeObject(checks,Formatting.Indented));
                AssetDatabase.SaveAssets();
                if(differences!=0 || bundle==null)throw new InvalidOperationException("OFF画像/Player shader smokeが不一致です。");
                EditorApplication.Exit(0);
            }
            catch(Exception e)
            { Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"FAILED.txt"),e.ToString());EditorApplication.Exit(1); }
        }

        private static int MissingScripts(GameObject root)
        {int count=0;foreach(Transform t in root.GetComponentsInChildren<Transform>(true))count+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);return count;}

        private static byte[] SaveImage(Camera camera,int width,int height,string name)
        {
            RenderTexture target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear) {antiAliasing=1};target.Create();
            RenderTexture old=RenderTexture.active;Texture2D texture=new Texture2D(width,height,TextureFormat.RGBA32,false,true);
            try
            {
                camera.aspect=(float)width/height;camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Output,name),texture.EncodeToPNG());
                return texture.GetRawTextureData<byte>().ToArray();
            }
            finally {camera.targetTexture=null;RenderTexture.active=old;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
