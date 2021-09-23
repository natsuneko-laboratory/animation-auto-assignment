using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEditor.UIElements;

using UnityEngine;
using UnityEngine.UIElements;

using Animation = UnityEngine.AnimationClip;

namespace NatsunekoLaboratory.HierarchyStalker
{
    public class HierarchyStalker : EditorWindow
    {
        private const string StyleGuid = "27316867ccdc0cb43ad4580e1b809c65";
        private const string ComponentStyleGuid = "b8036845117b0b44ab98068d378c9e5a";
        private const string XamlGuid = "b7750c11aaa0c954ea1fa8f1b6490f5a";

        [SerializeField]
        private Animation _animation;

        private bool _isTracking;
        private List<string> _selectionPaths;

        private List<GameObject> _selections;
        private SerializedObject _so;

        [SerializeField]
        private Transform _transform;

        [MenuItem("Window/Natsuneko Laboratory/Auto Animation Assignment")]
        public static void ShowWindow()
        {
            var window = GetWindow<HierarchyStalker>();
            window.titleContent = new GUIContent("Auto Animation Assignment");
            window.Show();
        }

        private static T LoadAssetByGuid<T>(string guid) where T : Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
        }

        public void CreateGUI()
        {
            _so = new SerializedObject(this);
            _so.Update();

            var root = rootVisualElement;
            root.styleSheets.Add(LoadAssetByGuid<StyleSheet>(StyleGuid));
            root.styleSheets.Add(LoadAssetByGuid<StyleSheet>(ComponentStyleGuid));

            var xaml = LoadAssetByGuid<VisualTreeAsset>(XamlGuid);
            var tree = xaml.CloneTree();
            tree.Bind(_so);
            root.Add(tree);

            var button = root.Query<Button>("tracking-button").First();
            button.clicked += () =>
            {
                _so.ApplyModifiedProperties();
                if (_isTracking)
                {
                    button.RemoveFromClassList("is-active");
                    button.text = "Start Tracking";
                    _isTracking = false;

                    EditorApplication.hierarchyChanged -= OnHierarchyChanged;
                    Selection.selectionChanged -= OnSelectionChanged;
                }
                else
                {
                    button.AddToClassList("is-active");
                    button.text = "Tracking Hierarchy...";
                    _isTracking = true;

                    EditorApplication.hierarchyChanged += OnHierarchyChanged;
                    Selection.selectionChanged += OnSelectionChanged;
                    EnforceCurrentSelections();
                }
            };
        }

        public void OnGUI()
        {
            var root = rootVisualElement;
            var button = root.Query<Button>("tracking-button").First();

            var disabled = _animation == null || _transform == null;
            button.SetEnabled(!disabled);
        }

        private void EnforceCurrentSelections()
        {
            Debug.Log(nameof(EnforceCurrentSelections));
            _selections = Selection.gameObjects.SelectMany(w => w.GetComponentsInChildren<Transform>(true).Select(v => v.gameObject)).ToList();
            _selectionPaths = _selections.Select(w => GetHierarchyPath(w.transform)).ToList();
        }

        private void OnHierarchyChanged()
        {
            var selections = Selection.gameObjects.SelectMany(w => w.GetComponentsInChildren<Transform>(true).Select(v => v.gameObject)).ToList();
            var selectionPaths = selections.Select(w => GetHierarchyPath(w.transform)).ToList();

            if (_selectionPaths.Intersect(selectionPaths).Count() == selectionPaths.Count)
                return;

            Debug.Log(nameof(OnHierarchyChanged));

            var excepts = selectionPaths.Except(_selectionPaths).Select(w =>
            {
                var changedToGameObject = selections.Select((v, i) => (GameObject: v, Index: i)).First(v => GetHierarchyPath(v.GameObject.transform) == w);
                var changedFromHierarchyPath = _selectionPaths[changedToGameObject.Index];
                return (From: changedFromHierarchyPath, To: changedToGameObject.GameObject);
            });

            FixAnimationPropertyPath(_animation, _transform, excepts.ToList());
            EnforceCurrentSelections();
        }

        private void OnSelectionChanged()
        {
            Debug.Log(nameof(OnSelectionChanged));
            _selections = Selection.gameObjects.SelectMany(w => w.GetComponentsInChildren<Transform>(true).Select(v => v.gameObject)).ToList();
            _selectionPaths = _selections.Select(w => GetHierarchyPath(w.transform)).ToList();
        }

        private static void FixAnimationPropertyPath(Animation animation, Transform transform, List<(string From, GameObject To)> changes)
        {
            string NormalizePath(string path)
            {
                var trash = GetHierarchyPath(transform);
                if (path.StartsWith(trash))
                    return path.Substring(trash.Length + 1);
                return path;
            }

            var bindings = AnimationUtility.GetCurveBindings(animation);
            var newBindings = new List<(EditorCurveBinding, AnimationCurve)>();
            foreach (var binding in bindings)
            {
                var newBinding = binding;
                foreach (var change in changes)
                    if (binding.path == NormalizePath(change.From))
                    {
                        var oldPath = binding.path;
                        var newPath = AnimationUtility.CalculateTransformPath(changes.First(w => NormalizePath(w.From) == oldPath).To.transform, transform);

                        Debug.Log($"The path of animation property `{binding.propertyName}` has been changed from `{oldPath}` to `{newPath}`");

                        newBinding.path = newPath;
                    }

                newBindings.Add((newBinding, AnimationUtility.GetEditorCurve(animation, binding)));
            }

            animation.ClearCurves();
            foreach (var (binding, curve) in newBindings)
                AnimationUtility.SetEditorCurve(animation, binding, curve);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static string GetHierarchyPath(Transform t)
        {
            var path = new List<string> { t.name };
            var parent = t.parent;

            while (parent != null)
            {
                path.Add(parent.name);
                parent = parent.parent;
            }

            path.Reverse();
            return string.Join("/", path);
        }
    }
}