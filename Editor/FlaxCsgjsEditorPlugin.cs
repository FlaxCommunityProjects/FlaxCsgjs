using System;
using System.Collections.Generic;
using FlaxCsgjs.Source;
using FlaxEditor;
using FlaxEditor.SceneGraph.Actors;
using FlaxEngine;

namespace ProceduralModeller.Source.FlaxCsgjs.Editor
{
    public class CsgEditorPlugin : EditorPlugin
    {
        /// <inheritdoc />
        public override void InitializeEditor()
        {
            base.InitializeEditor();

            Editor.SceneEditing.SelectionChanged += SceneEditing_SelectionChanged;
        }

        private void SceneEditing_SelectionChanged()
        {
            // When the user clicks on something
            if (Editor.SceneEditing.SelectionCount == 1)
            {
                var selectedNode = Editor.SceneEditing.Selection[0];
                if (selectedNode is StaticModelNode selectedModel)
                {
                    var csgScript = selectedModel.Actor.Parent.GetScript<CsgjsScript>();
                    if (csgScript && csgScript.IsRoot)
                    {
                        var mouseRay = Editor.MainTransformGizmo.Owner.MouseRay;
                        if (csgScript.Raycast(ref mouseRay, out float distance, out CsgjsScript script))
                        {
                            Editor.SceneEditing.Select(script.Actor);
                        }
                    }
                }
            }
        }

        /// <inheritdoc />
        public override void Deinitialize()
        {
            Editor.SceneEditing.SelectionChanged -= SceneEditing_SelectionChanged;
            base.Deinitialize();
        }
    }
}
