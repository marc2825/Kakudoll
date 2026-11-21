using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Core;

namespace VLat.HeadFrameExperiment
{
    internal static class ClientAvatarAssets
    {
        internal const string Folder = "Assets/VLatClientGenerated";
        internal const string Prefab = Folder + "/ClientDiagnostic.prefab";
        internal const string ReferencePrefab = Folder + "/ClientOffReference.prefab";
        private static readonly List<Vector3> positions = new List<Vector3>();
        private static readonly List<Color> colors = new List<Color>();
        private static readonly List<Vector2> uvs = new List<Vector2>();
        private static readonly List<int> triangles = new List<int>();
        private static readonly List<BoneWeight> weights = new List<BoneWeight>();
        private static readonly int[] BoxIndices = { 0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,1,2,6,1,6,5,0,4,7,0,7,3 };

        internal static GameObject Create()
        {
            if (AssetDatabase.IsValidFolder(Folder)) throw new InvalidOperationException("生成済みAvatarの上書きを拒否します。");
            AssetDatabase.CreateFolder("Assets", "VLatClientGenerated");
            GameObject root = new GameObject("VLatClientDiagnostic");
            List<Transform> bones = new List<Transform>(); Dictionary<HumanBodyBones,Transform> human = new Dictionary<HumanBodyBones,Transform>();
            Add(HumanBodyBones.Hips,root.transform,new Vector3(0,.95f,0),bones,human);
            Add(HumanBodyBones.Spine,human[HumanBodyBones.Hips],new Vector3(0,1.15f,0),bones,human);
            Add(HumanBodyBones.Chest,human[HumanBodyBones.Spine],new Vector3(0,1.4f,0),bones,human);
            Add(HumanBodyBones.Neck,human[HumanBodyBones.Chest],new Vector3(0,1.54f,0),bones,human);
            Add(HumanBodyBones.Head,human[HumanBodyBones.Neck],new Vector3(0,1.67f,0),bones,human);
            for (int side=0;side<2;++side)
            {
                float x=side==0?-1:1;
                HumanBodyBones shoulder=side==0?HumanBodyBones.LeftShoulder:HumanBodyBones.RightShoulder;
                HumanBodyBones arm=side==0?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm;
                HumanBodyBones forearm=side==0?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm;
                HumanBodyBones hand=side==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand;
                Add(shoulder,human[HumanBodyBones.Chest],new Vector3(x*.18f,1.42f,0),bones,human);
                Add(arm,human[shoulder],new Vector3(x*.25f,1.42f,0),bones,human);
                Add(forearm,human[arm],new Vector3(x*.51f,1.42f,0),bones,human);
                Add(hand,human[forearm],new Vector3(x*.76f,1.42f,0),bones,human);
                HumanBodyBones thigh=side==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg;
                HumanBodyBones shin=side==0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg;
                HumanBodyBones foot=side==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot;
                Add(thigh,human[HumanBodyBones.Hips],new Vector3(x*.1f,.9f,0),bones,human);
                Add(shin,human[thigh],new Vector3(x*.1f,.49f,0),bones,human);
                Add(foot,human[shin],new Vector3(x*.1f,.08f,.03f),bones,human);
            }
            Animator animator=root.AddComponent<Animator>();
            HumanBone[] mapping=new HumanBone[human.Count]; SkeletonBone[] skeleton=new SkeletonBone[bones.Count+1];
            skeleton[0]=new SkeletonBone { name=root.name,position=Vector3.zero,rotation=Quaternion.identity,scale=Vector3.one };
            int index=0;
            foreach(var pair in human)
            {
                mapping[index]=new HumanBone { boneName=pair.Value.name,humanName=HumanTrait.BoneName[(int)pair.Key],limit=new HumanLimit { useDefaultValues=true } };
                Transform t=pair.Value;
                skeleton[index+1]=new SkeletonBone { name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale };++index;
            }
            Avatar avatar=AvatarBuilder.BuildHumanAvatar(root,new HumanDescription { human=mapping,skeleton=skeleton,
                upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=.05f,legStretch=.05f,feetSpacing=0,hasTranslationDoF=false });
            if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("合成Humanoid rigが不正です。");
            avatar.name="VLatSyntheticHumanoid";AssetDatabase.CreateAsset(avatar,Folder+"/Humanoid.asset");animator.avatar=avatar;
            VRCAvatarDescriptor descriptor=root.AddComponent<VRCAvatarDescriptor>(); descriptor.ViewPosition=new Vector3(0,1.73f,.15f);
            root.AddComponent<PipelineManager>();
            Transform head=human[HumanBodyBones.Head],chest=human[HumanBodyBones.Chest];
            Transform child=Child("CanonicalHeadProxy",head),marker=Child("CanonicalConstraintMarker",head),proxy=Child("VrcHeadProxy",root.transform);
            proxy.SetPositionAndRotation(head.position,Quaternion.identity);
            VRCParentConstraint constraint=proxy.gameObject.AddComponent<VRCParentConstraint>();
            constraint.Sources.Add(new VRCConstraintSource(marker,1));constraint.GlobalWeight=1;constraint.IsActive=true;constraint.Locked=true;
            constraint.SolveInLocalSpace=false;constraint.ApplyConfigurationChanges();
            Texture2D font=Font();Material solid=new Material(Shader.Find("VLat/Experiments/ClientSolid"));
            AssetDatabase.CreateAsset(solid,Folder+"/SourceSolid.mat");
            Clear();
            foreach(var pair in human)
            {
                if (pair.Key==HumanBodyBones.Head) continue;
                Vector3 size=pair.Key==HumanBodyBones.Chest?new Vector3(.28f,.25f,.16f):new Vector3(.075f,.1f,.075f);
                Box(pair.Value.position,size,new Color(.3f,.35f,.4f,1),bones.IndexOf(pair.Value));
            }
            Renderer(root,"Body",Mesh("Body",root.transform,bones),bones,human[HumanBodyBones.Hips],solid);
            Clear();
            Box(head.position,new Vector3(.22f,.25f,.2f),new Color(.7f,.7f,.75f,1),bones.IndexOf(head));
            Box(head.position+new Vector3(.16f,.04f,0),new Vector3(.13f,.04f,.07f),Color.red,bones.IndexOf(head)); // unique +X mark
            Box(head.position+new Vector3(-.14f,0,0),new Vector3(.05f,.05f,.05f),Color.green,bones.IndexOf(head));
            Box(head.position+new Vector3(0,0,.16f),new Vector3(.025f,.025f,.13f),Color.blue,bones.IndexOf(head)); // +Z forward
            Material headMaterial=Diagnostic("Head",font,0,false,false);
            Renderer(root,"HeadProbe",Mesh("Head",root.transform,bones),bones,proxy,headMaterial);
            for(int card=0;card<3;++card)
            {
                Clear();
                Vector3 center=card==2?new Vector3(-.58f,1.13f,.22f):new Vector3(card==0?-.17f:.17f,1.19f,.23f);
                float width=card==2?.19f:.28f,height=width*64/34;
                Quad(center,width,height,bones.IndexOf(chest));
                Material material=Diagnostic("Card"+card,font,card,true,card==2);
                Renderer(root,"Card"+card,Mesh("Card"+card,root.transform,bones),bones,card==1?proxy:child,material);
            }
            VRCHeadChop chop=root.AddComponent<VRCHeadChop>();
            SerializedObject serialized=new SerializedObject(chop);
            SerializedProperty targets=serialized.FindProperty("targetBones");targets.arraySize=1;
            SerializedProperty target=targets.GetArrayElementAtIndex(0);
            SerializedProperty transform=target.FindPropertyRelative("transform"),scale=target.FindPropertyRelative("scaleFactor"),condition=target.FindPropertyRelative("applyCondition");
            if(transform==null || scale==null || condition==null) throw new InvalidOperationException("SDK3.10.5 HeadChop schemaが一致しません。");
            transform.objectReferenceValue=head;scale.floatValue=1;condition.enumValueIndex=0;
            serialized.FindProperty("globalScaleFactor").floatValue=1;serialized.ApplyModifiedPropertiesWithoutUndo();chop.enabled=false;
            Expressions(root,descriptor);
            PrefabUtility.SaveAsPrefabAsset(root,Prefab);
            // A second generated prefab is the original Hips-root, solid-material OFF reference.
            SkinnedMeshRenderer h=root.transform.Find("HeadProbe").GetComponent<SkinnedMeshRenderer>();h.rootBone=human[HumanBodyBones.Hips];h.sharedMaterial=solid;
            root.name="VLatClientOffReference";
            VRCExpressionsMenu referenceMenu=UnityEngine.Object.Instantiate(descriptor.expressionsMenu);
            referenceMenu.controls.RemoveAt(0);AssetDatabase.CreateAsset(referenceMenu,Folder+"/ReferenceMenu.asset");descriptor.expressionsMenu=referenceMenu;
            PrefabUtility.SaveAsPrefabAsset(root,ReferencePrefab);
            UnityEngine.Object.DestroyImmediate(root);AssetDatabase.SaveAssets();
            return (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
        }

        private static void Expressions(GameObject root,VRCAvatarDescriptor descriptor)
        {
            VRCExpressionParameters parameters=ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.parameters=new[] { new VRCExpressionParameters.Parameter { name="VLatEffect",valueType=VRCExpressionParameters.ValueType.Bool,defaultValue=0,saved=false,networkSynced=true },
                new VRCExpressionParameters.Parameter { name="KeepHead",valueType=VRCExpressionParameters.ValueType.Bool,defaultValue=0,saved=false,networkSynced=true } };
            AssetDatabase.CreateAsset(parameters,Folder+"/Parameters.asset");
            VRCExpressionsMenu menu=ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls=new List<VRCExpressionsMenu.Control> {
                new VRCExpressionsMenu.Control { name="Probe ON (+X)",type=VRCExpressionsMenu.Control.ControlType.Toggle,parameter=new VRCExpressionsMenu.Control.Parameter { name="VLatEffect" },value=1 },
                new VRCExpressionsMenu.Control { name="Keep Head (test)",type=VRCExpressionsMenu.Control.ControlType.Toggle,parameter=new VRCExpressionsMenu.Control.Parameter { name="KeepHead" },value=1 } };
            AssetDatabase.CreateAsset(menu,Folder+"/Menu.asset");
            AnimatorController fx=AnimatorController.CreateAnimatorControllerAtPath(Folder+"/FX.controller");
            fx.AddParameter("VLatEffect",AnimatorControllerParameterType.Bool);fx.AddParameter("KeepHead",AnimatorControllerParameterType.Bool);
            for(int layer=0;layer<2;++layer)
            {
                if(layer==1)fx.AddLayer("Head Chop override");
                var layers=fx.layers;layers[layer].defaultWeight=1;fx.layers=layers;
                AnimatorStateMachine sm=fx.layers[layer].stateMachine;
                var off=sm.AddState("Off");var on=sm.AddState("On");sm.defaultState=off;
                for(int state=0;state<2;++state)
                {
                    AnimationClip clip=new AnimationClip { name=(layer==0?"Effect":"HeadKeep")+state };
                    if(layer==0)
                        foreach(string name in new[] { "HeadProbe","Card0","Card1","Card2" })
                            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(name,typeof(SkinnedMeshRenderer),"material._Effect"),AnimationCurve.Constant(0,1,state));
                    else AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(VRCHeadChop),"m_Enabled"),AnimationCurve.Constant(0,1,state));
                    AssetDatabase.CreateAsset(clip,Folder+"/"+clip.name+".anim");
                    AnimatorState target=state==0?off:on;target.writeDefaultValues=false;target.motion=clip;
                }
                AnimatorStateTransition enter=off.AddTransition(on),leave=on.AddTransition(off);
                enter.hasExitTime=false;leave.hasExitTime=false;enter.duration=0;leave.duration=0;
                enter.AddCondition(AnimatorConditionMode.If,0,layer==0?"VLatEffect":"KeepHead");
                leave.AddCondition(AnimatorConditionMode.IfNot,0,layer==0?"VLatEffect":"KeepHead");
            }
            descriptor.customExpressions=true;descriptor.expressionParameters=parameters;descriptor.expressionsMenu=menu;
            descriptor.customizeAnimationLayers=true;
            descriptor.baseAnimationLayers=new[] {
                new VRCAvatarDescriptor.CustomAnimLayer { type=VRCAvatarDescriptor.AnimLayerType.Base,isDefault=true },
                new VRCAvatarDescriptor.CustomAnimLayer { type=VRCAvatarDescriptor.AnimLayerType.Additive,isDefault=true },
                new VRCAvatarDescriptor.CustomAnimLayer { type=VRCAvatarDescriptor.AnimLayerType.Gesture,isDefault=true },
                new VRCAvatarDescriptor.CustomAnimLayer { type=VRCAvatarDescriptor.AnimLayerType.Action,isDefault=true },
                new VRCAvatarDescriptor.CustomAnimLayer { type=VRCAvatarDescriptor.AnimLayerType.FX,isDefault=false,animatorController=fx } };
        }
        private static void Add(HumanBodyBones name,Transform parent,Vector3 world,List<Transform> bones,Dictionary<HumanBodyBones,Transform> human)
        { Transform t=Child(name.ToString(),parent);t.position=world;bones.Add(t);human.Add(name,t); }
        private static Transform Child(string name,Transform parent) { Transform t=new GameObject(name).transform;t.SetParent(parent,false);return t; }
        private static Material Diagnostic(string name,Texture2D font,int id,bool card,bool strict)
        {
            Material m=new Material(Shader.Find(strict?"VLat/Experiments/ClientNoStereoArrays":"VLat/Experiments/ClientDiagnostic"));
            m.name=name;m.SetTexture("_Font",font);m.SetFloat("_Card",card?1:0);m.SetFloat("_CardId",id);m.SetFloat("_RequireStereoCenter",strict?1:0);
            HeadFrameMath.SetCalibration(m,Matrix4x4.identity);AssetDatabase.CreateAsset(m,Folder+"/"+name+".mat");return m;
        }
        private static SkinnedMeshRenderer Renderer(GameObject root,string name,Mesh mesh,List<Transform> bones,Transform frame,Material material)
        {
            Transform t=Child(name,root.transform);SkinnedMeshRenderer r=t.gameObject.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh=mesh;r.bones=bones.ToArray();r.rootBone=frame;r.sharedMaterial=material;r.quality=SkinQuality.Bone4;r.updateWhenOffscreen=true;
            r.localBounds=new Bounds(new Vector3(0,-.7f,0),new Vector3(4,6,4)); // diagnostic bounds only
            return r;
        }
        private static void Clear() { positions.Clear();colors.Clear();uvs.Clear();triangles.Clear();weights.Clear(); }
        private static void Box(Vector3 center,Vector3 size,Color color,int bone)
        {
            int start=positions.Count;
            for(int i=0;i<8;++i)
            {
                Vector3 p=center+Vector3.Scale(size*.5f,new Vector3((i==1||i==2||i==5||i==6)?1:-1,(i==2||i==3||i==6||i==7)?1:-1,i>=4?1:-1));
                positions.Add(p);colors.Add(color);uvs.Add(Vector2.zero);weights.Add(new BoneWeight { boneIndex0=bone,weight0=1 });
            }
            foreach(int i in BoxIndices)triangles.Add(start+i);
        }
        private static void Quad(Vector3 center,float width,float height,int bone)
        {
            int start=positions.Count;
            foreach(Vector2 uv in new[] { new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1) })
            { positions.Add(center+new Vector3((uv.x-.5f)*width,(uv.y-.5f)*height,0));colors.Add(Color.white);uvs.Add(new Vector2(1-uv.x,uv.y));weights.Add(new BoneWeight { boneIndex0=bone,weight0=1 }); }
            triangles.AddRange(new[] {start,start+2,start+1,start,start+3,start+2});
        }
        private static Mesh Mesh(string name,Transform renderer,List<Transform> bones)
        {
            Mesh m=new Mesh { name="Client"+name };m.SetVertices(positions);m.SetColors(colors);m.SetUVs(0,uvs);m.SetTriangles(triangles,0);m.boneWeights=weights.ToArray();
            Matrix4x4[] bind=new Matrix4x4[bones.Count];for(int i=0;i<bind.Length;++i)bind[i]=bones[i].worldToLocalMatrix*renderer.localToWorldMatrix;m.bindposes=bind;
            m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.CreateAsset(m,Folder+"/"+name+".asset");return m;
        }
        private static Texture2D Font()
        {
            var patterns=new Dictionary<char,string> {
                ['0']="01110/10001/10011/10101/11001/10001/01110",['1']="00100/01100/00100/00100/00100/00100/01110",
                ['2']="01110/10001/00001/00010/00100/01000/11111",['3']="11110/00001/00001/01110/00001/00001/11110",
                ['4']="00010/00110/01010/10010/11111/00010/00010",['5']="11111/10000/10000/11110/00001/00001/11110",
                ['6']="01110/10000/10000/11110/10001/10001/01110",['7']="11111/00001/00010/00100/01000/01000/01000",
                ['8']="01110/10001/10001/01110/10001/10001/01110",['9']="01110/10001/10001/01111/00001/00001/01110",
                ['Y']="10001/10001/01010/00100/00100/00100/00100",['P']="11110/10001/10001/11110/10000/10000/10000",
                ['W']="10001/10001/10001/10101/10101/11011/10001",['S']="01111/10000/10000/01110/00001/00001/11110",
                ['C']="01110/10001/10000/10000/10000/10001/01110",['E']="11111/10000/10000/11110/10000/10000/11111",
                ['+']="00000/00100/00100/11111/00100/00100/00000",['-']="00000/00000/00000/11111/00000/00000/00000",['.']="00000/00000/00000/00000/00000/00100/00100" };
            Texture2D t=new Texture2D(128,64,TextureFormat.RGBA32,false,true);Color[] pixels=new Color[128*64];
            foreach(var pair in patterns)
            {
                int ox=(pair.Key%16)*8,oy=(7-pair.Key/16)*8;string[] rows=pair.Value.Split('/');
                for(int y=0;y<7;++y)for(int x=0;x<5;++x)if(rows[y][x]=='1')pixels[(oy+7-y)*128+ox+x+1]=Color.white;
            }
            t.SetPixels(pixels);t.Apply();File.WriteAllBytes(Folder+"/Font.png",t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(Folder+"/Font.png");TextureImporter importer=(TextureImporter)AssetImporter.GetAtPath(Folder+"/Font.png");
            importer.sRGBTexture=false;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Point;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Font.png");
        }
    }
}
