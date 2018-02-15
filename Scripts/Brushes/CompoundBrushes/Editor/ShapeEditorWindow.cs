﻿#if UNITY_EDITOR || RUNTIME_CSG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Sabresaurus.SabreCSG.ShapeEditor
{
    /// <summary>
    /// The 2D Shape Editor Window.
    /// </summary>
    /// <remarks>Inspired by Unreal Editor 1 (1998). Created by Henry de Jongh for SabreCSG.</remarks>
    /// <seealso cref="UnityEditor.EditorWindow"/>
    public class ShapeEditorWindow : EditorWindow
    {
        //private class FakeUndoObject : UnityEngine.Object
        //{
        //    // todo
        //}

        /// <summary>
        /// The currently loaded project.
        /// </summary>
        private Project project = new Project();

        /// <summary>
        /// The viewport scroll position.
        /// </summary>
        private Vector2 viewportScroll = new Vector2(100.0f, 100.0f);

        /// <summary>
        /// The initialized flag, used to scroll the project into the center of the window.
        /// </summary>
        private bool initialized = false;

        /// <summary>
        /// The grid scale.
        /// </summary>
        private int gridScale = 16;

        /// <summary>
        /// The currently selected objects.
        /// </summary>
        private List<ISelectable> selectedObjects = new List<ISelectable>();

        /// <summary>
        /// The line material.
        /// </summary>
        private Material lineMaterial;

        /// <summary>
        /// The grid material.
        /// </summary>
        private Material gridMaterial;

        /// <summary>
        /// The currently selected segments.
        /// </summary>
        private IEnumerable<Segment> selectedSegments
        {
            get
            {
                return selectedObjects.OfType<Segment>();
            }
        }

        /// <summary>
        /// Gets a value indicating whether the global pivot is selected.
        /// </summary>
        /// <value><c>true</c> if the global pivot is selected; otherwise, <c>false</c>.</value>
        private bool isGlobalPivotSelected
        {
            get
            {
                return IsObjectSelected(project.globalPivot);
            }
        }

        /// <summary>
        /// Determines whether the specified object is selected.
        /// </summary>
        /// <param name="obj">The object to check.</param>
        /// <returns><c>true</c> if the specified object is selected; otherwise, <c>false</c>.</returns>
        private bool IsObjectSelected(ISelectable obj)
        {
            return selectedObjects.Contains(obj);
        }

        /// <summary>
        /// Clears the selection of <typeparamref name="T" /> elements.
        /// </summary>
        /// <typeparam name="T">The type of element to be deselected.</typeparam>
        private void ClearSelectionOf<T>() where T : ISelectable
        {
            selectedObjects.RemoveAll((obj) => obj.GetType() == typeof(T));
        }

        /// <summary>
        /// Gets the shape that the segment belongs to.
        /// </summary>
        /// <param name="segment">The segment to search for.</param>
        /// <returns>The shape that the segment belongs to.</returns>
        private Shape GetShapeOfSegment(Segment segment)
        {
            return project.shapes.Where((shape) => shape.segments.Contains(segment)).FirstOrDefault();
        }

        /// <summary>
        /// Gets the previous segment.
        /// </summary>
        /// <param name="segment">The segment to find the previous segment for.</param>
        /// <returns>The previous segment (wraps around).</returns>
        private Segment GetPreviousSegment(Segment segment)
        {
            Shape parent = GetShapeOfSegment(segment);
            int index = parent.segments.IndexOf(segment);
            if (index - 1 < 0)
                return parent.segments[parent.segments.Count - 1];
            return parent.segments[index - 1];
        }

        /// <summary>
        /// Gets the next segment.
        /// </summary>
        /// <param name="segment">The segment to find the next segment for.</param>
        /// <returns>The next segment (wraps around).</returns>
        private Segment GetNextSegment(Segment segment)
        {
            Shape parent = GetShapeOfSegment(segment);
            int index = parent.segments.IndexOf(segment);
            if (index + 1 > parent.segments.Count - 1)
                return parent.segments[0];
            return parent.segments[index + 1];
        }

        /// <summary>
        /// Gets the segment at grid position.
        /// </summary>
        /// <param name="x">The x-coordinate on the grid.</param>
        /// <param name="y">The y-coordinate on the grid.</param>
        /// <returns>The segment if found else null.</returns>
        private ISelectable GetObjectAtGridPosition(Vector2Int position)
        {
            // the global pivot point has the highest selection priority.
            if (project.globalPivot.position == position)
                return project.globalPivot;
            // the bezier segment pivots have medium-high priority.
            foreach (Shape shape in project.shapes)
            {
                Segment segment = shape.segments.FirstOrDefault((s) => s.type == SegmentType.Bezier && s.bezierPivot1.position == position);
                if (segment != null)
                    return segment.bezierPivot1;
                segment = shape.segments.FirstOrDefault((s) => s.type == SegmentType.Bezier && s.bezierPivot2.position == position);
                if (segment != null)
                    return segment.bezierPivot2;
            }
            // the segments have the medium-low priority.
            foreach (Shape shape in project.shapes)
            {
                Segment segment = shape.segments.FirstOrDefault((s) => s.position == position);
                if (segment != null)
                    return segment;
            }
            // the shape pivots have lowest priority.
            foreach (Shape shape in project.shapes)
            {
                if (shape.pivot.position == position)
                    return shape.pivot;
            }
            // nothing was found.
            return null;
        }

        [MenuItem("Window/SabreCSG/2D Shape Editor")]
        public static void Init()
        {
            // get existing open window or if none, make a new one:
            ShapeEditorWindow window = GetWindow<ShapeEditorWindow>();
            window.minSize = new Vector2(800, 600);
            window.Show();
            window.titleContent = new GUIContent("Shape Editor");
            window.minSize = new Vector2(128, 128);
        }

        public static ShapeEditorWindow InitAndGetHandle()
        {
            Init();
            return GetWindow<ShapeEditorWindow>();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseDrag)
            {
                // move object around with the left mouse button.
                if (Event.current.button == 0)
                {
                    Vector2Int grid = ScreenPointToGrid(new Vector3(Event.current.mousePosition.x, Event.current.mousePosition.y));
                    if (GetViewportRect().Contains(Event.current.mousePosition))
                    {
                        // move the global pivot.
                        if (isGlobalPivotSelected)
                        {
                            project.globalPivot.position = grid;
                            this.Repaint();
                        }
                        // move an entire shape by its pivot.
                        foreach (Shape shape in project.shapes)
                        {
                            if (IsObjectSelected(shape.pivot))
                            {
                                Vector2Int delta = grid - shape.pivot.position;
                                shape.pivot.position = grid;
                                foreach (Segment segment in shape.segments)
                                {
                                    // move segment.
                                    segment.position += delta;
                                    // move bezier pivot handles.
                                    segment.bezierPivot1.position += delta;
                                    segment.bezierPivot2.position += delta;
                                }
                                this.Repaint();
                            }
                            else
                            {
                                // if not dragging a shape by its pivot, center it.
                                // this is not quite the right place to do it but it works well enough.
                                shape.CalculatePivotPosition();
                                this.Repaint();
                            }

                            // move the bezier curves of a segment.
                            foreach (Segment segment in shape.segments)
                            {
                                if (segment.type != SegmentType.Bezier) continue;
                                if (IsObjectSelected(segment.bezierPivot1))
                                    segment.bezierPivot1.position = grid;
                                if (IsObjectSelected(segment.bezierPivot2))
                                    segment.bezierPivot2.position = grid;
                                this.Repaint();
                            }
                        }
                        // move a segment by its pivot.
                        foreach (Segment segment in selectedSegments)
                        {
                            segment.position = grid;
                            this.Repaint();
                        }
                    }
                }

                // pan the viewport around with the right mouse button.
                if (Event.current.button == 1)
                {
                    viewportScroll += Event.current.delta;
                    this.Repaint();
                }
            }

            if (Event.current.type == EventType.MouseDown)
            {
                if (Event.current.button == 0 && GetViewportRect().Contains(Event.current.mousePosition))
                {
                    // if the user is not holding CTRL or SHIFT we clear the selected objects.
                    if ((Event.current.modifiers & EventModifiers.Control) == 0 && (Event.current.modifiers & EventModifiers.Shift) == 0)
                        selectedObjects.Clear();

                    // try finding an object under the mouse cursor.
                    Vector2Int grid = ScreenPointToGrid(new Vector3(Event.current.mousePosition.x, Event.current.mousePosition.y));
                    ISelectable found = GetObjectAtGridPosition(grid);
                    if (found != null && !selectedObjects.Contains(found))
                        // select the object.
                        selectedObjects.Add(found);
                    this.Repaint();
                }
            }

            // implement keyboard shortcuts.
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.N)
                {
                    OnNew();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.O)
                {
                    OnOpen();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.S)
                {
                    OnSave();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.R && Event.current.modifiers != 0)
                {
                    OnRotate90Left();
                    Event.current.Use();
                }
                else if (Event.current.keyCode == KeyCode.R)
                {
                    OnRotate90Right();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.H)
                {
                    OnFlipHorizontally();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.V)
                {
                    OnFlipVertically();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.Plus || Event.current.keyCode == KeyCode.KeypadPlus)
                {
                    OnZoomIn();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.Minus || Event.current.keyCode == KeyCode.KeypadMinus)
                {
                    OnZoomOut();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.A)
                {
                    OnShapeCreate();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.I)
                {
                    OnSegmentInsert();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.Delete)
                {
                    OnDelete();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.L)
                {
                    OnSegmentLinear();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.B)
                {
                    OnSegmentBezier();
                    Event.current.Use();
                }
                if (Event.current.keyCode == KeyCode.D)
                {
                    OnSegmentBezierDetail();
                    Event.current.Use();
                }
            }

            //if(Event.current.type == EventType.ValidateCommand && Event.current.commandName == "UndoRedoPerformed")
            //{
            //    // we don't want to use the unity editor undo function.
            //    Debug.Log(Event.current.commandName);
            //    Event.current.Use();
            //}


            if (Event.current.type == EventType.Repaint)
            {
                if (!initialized)
                {
                    initialized = true;
                    // scroll to the center of the screen.
                    viewportScroll = new Vector2(Screen.safeArea.width / 2.0f, Screen.safeArea.height / 2.0f);
                }

                GUI.color = Color.white;
                GUI.DrawTexture(GetViewportRect(), EditorGUIUtility.whiteTexture);
                EditorGUIUtility.AddCursorRect(GetViewportRect(), MouseCursor.MoveArrow);

                if (lineMaterial == null)
                {
                    var shader = Shader.Find("SabreCSG/ShapeEditorLine");
                    lineMaterial = new Material(shader);
                }

                if (gridMaterial == null)
                {
                    var shader = Shader.Find("SabreCSG/ShapeEditorGrid");
                    gridMaterial = new Material(shader);
                }

                // draw the grid using the special grid shader:
                bool docked = isDocked;
                gridMaterial.SetFloat("_OffsetX", GetViewportRect().x + (docked ? 2 : 0)); // why is this neccesary, what's moving?
                gridMaterial.SetFloat("_OffsetY", GetViewportRect().y + (docked ? 0 : 3)); // why is this neccesary, what's moving?
                gridMaterial.SetFloat("_ScrollX", viewportScroll.x);
                gridMaterial.SetFloat("_ScrollY", viewportScroll.y);
                gridMaterial.SetFloat("_Zoom", gridScale);
                gridMaterial.SetPass(0);

                GL.Begin(GL.QUADS);
                GL.LoadIdentity();
                Rect viewportRect = GetViewportRect();
                GL.Color(Color.red);
                GlDrawRectangle(viewportRect.x, viewportRect.y, viewportRect.width, viewportRect.height);
                GL.End();
                ///////////////////////////////////////////////////////////////////////////////////

                lineMaterial.SetPass(0);

                GL.Begin(GL.QUADS);
                GL.LoadIdentity();

                // this should be done in the shader instead:
                // draw the center lines of the grid:

                Vector2 center = GridPointToScreen(new Vector2Int(0, 0));
                GL.Color(new Color(0.882f, 0.882f, 0.882f));
                GlDrawLine(3.0f, 0.0f, center.y, viewportRect.width, center.y);
                GlDrawLine(3.0f, center.x, viewportRect.y, center.x, viewportRect.y + viewportRect.height);

                // draw all of the segments:
                foreach (Shape shape in project.shapes)
                {
                    foreach (Segment segment in shape.segments)
                    {
                        GL.Color(new Color(0.502f, 0.502f, 0.502f));
                        Segment next = GetNextSegment(segment);
                        float thickness = 1.0f;
                        bool isBezier1Selected = false;
                        bool isBezier2Selected = false;
                        if (segment.type == SegmentType.Bezier)
                        {
                            isBezier1Selected = IsObjectSelected(segment.bezierPivot1);
                            isBezier2Selected = IsObjectSelected(segment.bezierPivot2);
                        }
                        if (IsObjectSelected(segment) || isBezier1Selected || isBezier2Selected)
                        {
                            thickness = 3.0f;
                        }
                        if (segment.type == SegmentType.Linear)
                        {
                            Vector2 p1 = GridPointToScreen(segment.position);
                            Vector2 p2 = GridPointToScreen(next.position);
                            GlDrawLine(thickness, p1.x, p1.y, p2.x, p2.y);
                        }
                        if (segment.type == SegmentType.Bezier)
                        {
                            Vector2 p1 = GridPointToScreen(segment.position);
                            Vector2 p2 = GridPointToScreen(next.position);
                            Vector2 p3 = GridPointToScreen(segment.bezierPivot1.position);
                            Vector2 p4 = GridPointToScreen(segment.bezierPivot2.position);
                            GlDrawBezier(thickness, p1, p3, p4, p2, segment.bezierDetail + 1);

                            // draw the lines towards the pivots of the bezier curve.
                            GL.Color(Color.blue);
                            GlDrawLine(isBezier1Selected ? 2.0f : 1.0f, p1.x, p1.y, p3.x, p3.y);
                            GlDrawLine(isBezier2Selected ? 2.0f : 1.0f, p2.x, p2.y, p4.x, p4.y);
                        }
                    }
                }

                GL.End();

                // draw the handles on the corners of the segments.
                Handles.color = Color.white;
                foreach (Shape shape in project.shapes)
                {
                    foreach (Segment segment in shape.segments)
                    {
                        // draw pivots of the segments.
                        Vector2 segmentScreenPosition = GridPointToScreen(segment.position);
                        Handles.DrawSolidRectangleWithOutline(new Rect(segmentScreenPosition.x - 4.0f, segmentScreenPosition.y - 4.0f, 8.0f, 8.0f), Color.white, IsObjectSelected(segment) ? Color.red : Color.black);

                        // draw bezier pivots for bezier segments.
                        if (segment.type == SegmentType.Bezier)
                        {
                            segmentScreenPosition = GridPointToScreen(segment.bezierPivot1.position);
                            Handles.DrawSolidRectangleWithOutline(new Rect(segmentScreenPosition.x - 4.0f, segmentScreenPosition.y - 4.0f, 8.0f, 8.0f), Color.white, IsObjectSelected(segment.bezierPivot1) ? Color.red : Color.blue);
                            segmentScreenPosition = GridPointToScreen(segment.bezierPivot2.position);
                            Handles.DrawSolidRectangleWithOutline(new Rect(segmentScreenPosition.x - 4.0f, segmentScreenPosition.y - 4.0f, 8.0f, 8.0f), Color.white, IsObjectSelected(segment.bezierPivot2) ? Color.red : Color.blue);
                        }
                    }

                    // draw the shape pivot point.
                    Vector2 centerScreenPosition = GridPointToScreen(shape.pivot.position);
                    Handles.DrawSolidRectangleWithOutline(new Rect(centerScreenPosition.x - 4.0f, centerScreenPosition.y - 4.0f, 8.0f, 8.0f), Color.white, IsObjectSelected(shape.pivot) ? Color.red : Color.magenta);
                }

                // draw the global pivot point.
                Vector2 pivotScreenPosition = GridPointToScreen(project.globalPivot.position);
                Handles.DrawSolidRectangleWithOutline(new Rect(pivotScreenPosition.x - 4.0f, pivotScreenPosition.y - 4.0f, 8.0f, 8.0f), Color.white, isGlobalPivotSelected ? Color.red : Color.green);
            }

            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUIStyle createBrushStyle = new GUIStyle(EditorStyles.toolbarButton);
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorNewTexture, "New Project (N)"), createBrushStyle))
            {
                OnNew();
            }
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorOpenTexture, "Open Project (O)"), createBrushStyle))
            {
                OnOpen();
            }
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorSaveTexture, "Save Project (S)"), createBrushStyle))
            {
                OnSave();
            }

            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorRotate90LeftTexture, "Rotate 90° Left Around Pivot (SHIFT + R)"), createBrushStyle))
            {
                OnRotate90Left();
            }

            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorRotate90RightTexture, "Rotate 90° Right Around Pivot (R)"), createBrushStyle))
            {
                OnRotate90Right();
            }

            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorFlipVerticallyTexture, "Flip Vertically At Pivot (V)"), createBrushStyle))
            {
                OnFlipVertically();
            }
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorFlipHorizontallyTexture, "Flip Horizontally At Pivot (H)"), createBrushStyle))
            {
                OnFlipHorizontally();
            }

            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorZoomInTexture, "Zoom In (+)"), createBrushStyle))
            {
                OnZoomIn();
            }
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorZoomOutTexture, "Zoom Out (-)"), createBrushStyle))
            {
                OnZoomOut();
            }

            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorShapeCreateTexture, "Add New Shape (A)"), createBrushStyle))
            {
                OnShapeCreate();
            }

            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorSegmentInsertTexture, "Split Segment(s) (I)"), createBrushStyle))
            {
                OnSegmentInsert();
            }
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorDeleteTexture, "Delete Segment(s) or Shape(s) (DEL)"), createBrushStyle))
            {
                OnDelete();
            }

            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorSegmentLinearTexture, "Linear Segment (L)"), createBrushStyle))
            {
                OnSegmentLinear();
            }
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorSegmentBezierTexture, "Bezier Segment (B)"), createBrushStyle))
            {
                OnSegmentBezier();
            }
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorSegmentBezierDetailTexture, "Bezier Detail Settings (D)"), createBrushStyle))
            {
                OnSegmentBezierDetail();
            }

            GUI.enabled = (Selection.activeGameObject && Selection.activeGameObject.HasComponent<ShapeEditor.ShapeEditorBrush>());
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorCreatePolygonTexture, "Create Polygon"), createBrushStyle))
            {
                OnCreatePolygon();
            }
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorExtrudeRevolveTexture, "Revolve Shape"), createBrushStyle))
            {
                OnExtrudeRevolve();
            }
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorExtrudeShapeTexture, "Extrude Shape"), createBrushStyle))
            {
                OnExtrudeShape();
            }
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorExtrudePointTexture, "Extrude To Point"), createBrushStyle))
            {
                OnExtrudePoint();
            }
            if (GUILayout.Button(new GUIContent(SabreCSGResources.ShapeEditorExtrudeBevelTexture, "Extrude Bevelled"), createBrushStyle))
            {
                OnExtrudeBevel();
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Rotates the segments by an amount of degrees around a pivot position.
        /// </summary>
        /// <param name="degrees">The degrees to rotate the segments by.</param>
        /// <param name="pivot">The pivot to rotate around.</param>
        private void RotateSegments(float degrees, Vector2Int pivot)
        {
            foreach (Shape shape in project.shapes)
            {
                foreach (Segment segment in shape.segments)
                {
                    float s = Mathf.Sin(Mathf.Deg2Rad * degrees);
                    float c = Mathf.Cos(Mathf.Deg2Rad * degrees);

                    // translate point back to origin:
                    segment.position -= pivot;
                    // translate the bezier pivots:
                    segment.bezierPivot1.position -= pivot;
                    segment.bezierPivot2.position -= pivot;

                    // rotate point.
                    float x1new = segment.position.x * c - segment.position.y * s;
                    float y1new = segment.position.x * s + segment.position.y * c;
                    // rotate bezier pivot 1.
                    float x2new = segment.bezierPivot1.position.x * c - segment.bezierPivot1.position.y * s;
                    float y2new = segment.bezierPivot1.position.x * s + segment.bezierPivot1.position.y * c;
                    // rotate bezier pivot 2.
                    float x3new = segment.bezierPivot2.position.x * c - segment.bezierPivot2.position.y * s;
                    float y3new = segment.bezierPivot2.position.x * s + segment.bezierPivot2.position.y * c;

                    // translate point back:
                    segment.position = new Vector2Int(Mathf.RoundToInt(x1new + pivot.x), Mathf.RoundToInt(y1new + pivot.y));
                    // translate bezier pivots back:
                    segment.bezierPivot1.position = new Vector2Int(Mathf.RoundToInt(x2new + pivot.x), Mathf.RoundToInt(y2new + pivot.y));
                    segment.bezierPivot2.position = new Vector2Int(Mathf.RoundToInt(x3new + pivot.x), Mathf.RoundToInt(y3new + pivot.y));
                }

                // recalculate the pivot position of the shape.
                shape.CalculatePivotPosition();
            }
        }



        /// <summary>
        /// Called when the new button is pressed. Will reset the shape.
        /// </summary>
        private void OnNew()
        {
            if (EditorUtility.DisplayDialog("2D Shape Editor", "Are you sure you wish to create a new project?", "Yes", "No"))
            {
                // create a new project.
                project = new Project();
            }
        }

        /// <summary>
        /// Called when the open button is pressed. Will let the user open an existing shape.
        /// </summary>
        private void OnOpen()
        {
            try
            {
                string path = EditorUtility.OpenFilePanel("Load 2D Shape Editor Project", "", "sabre2d");
                if (path.Length != 0)
                {
                    Project proj = JsonUtility.FromJson<Project>(File.ReadAllText(path));
                    // incompatible project version detected!
                    if (proj.version != 1)
                    {
                        if (EditorUtility.DisplayDialog("2D Shape Editor", "Unsupported project version! Would you like to try loading it anyway?", "Yes", "No"))
                            project = proj;
                        Repaint();
                    }
                    else
                    {
                        project = proj;
                        Repaint();
                    }
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("2D Shape Editor", "An exception occured while loading the project:\r\n" + ex.Message, "Ohno!");
            }
        }

        /// <summary>
        /// Called when the save button is pressed. Will let the user save the shape.
        /// </summary>
        private void OnSave()
        {
            try
            {
                string path = EditorUtility.SaveFilePanel("Save 2D Shape Editor Project", "", "Project", "sabre2d");
                if (path.Length != 0)
                {
                    string json = JsonUtility.ToJson(project);
                    File.WriteAllText(path, json);
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("2D Shape Editor", "An exception occured while saving the project:\r\n" + ex.Message, "Ohno!");
            }
        }

        /// <summary>
        /// Called when the rotate 90 left button is pressed. Will rotate the shape left by 90 degrees around the pivot point.
        /// </summary>
        private void OnRotate90Left()
        {
            RotateSegments(-90, project.globalPivot.position);
        }

        /// <summary>
        /// Called when the rotate 90 right button is pressed. Will rotate the shape right by 90 degrees around the pivot point.
        /// </summary>
        private void OnRotate90Right()
        {
            RotateSegments(90, project.globalPivot.position);
        }

        /// <summary>
        /// Called when the flip vertically button is pressed. Will flip the shape vertically at the pivot point.
        /// </summary>
        private void OnFlipVertically()
        {
            // store this flip inside of the project.
            project.flipVertically = !project.flipVertically;

            foreach (Shape shape in project.shapes)
            {
                foreach (Segment segment in shape.segments)
                {
                    // flip segment.
                    segment.position = new Vector2Int(segment.position.x, -segment.position.y + (project.globalPivot.position.y * 2));
                    // flip bezier pivot handles.
                    segment.bezierPivot1.position = new Vector2Int(segment.bezierPivot1.position.x, -segment.bezierPivot1.position.y + (project.globalPivot.position.y * 2));
                    segment.bezierPivot2.position = new Vector2Int(segment.bezierPivot2.position.x, -segment.bezierPivot2.position.y + (project.globalPivot.position.y * 2));
                }

                // recalculate the pivot position of the shape.
                shape.CalculatePivotPosition();
            }
        }

        /// <summary>
        /// Called when the flip horizontally button is pressed. Will flip the shape horizontally at the pivot point.
        /// </summary>
        private void OnFlipHorizontally()
        {
            // store this flip inside of the project.
            project.flipHorizontally = !project.flipHorizontally;

            foreach (Shape shape in project.shapes)
            {
                foreach (Segment segment in shape.segments)
                {
                    // flip segment.
                    segment.position = new Vector2Int(-segment.position.x + (project.globalPivot.position.x * 2), segment.position.y);
                    // flip bezier pivot handles.
                    segment.bezierPivot1.position = new Vector2Int(-segment.bezierPivot1.position.x + (project.globalPivot.position.x * 2), segment.bezierPivot1.position.y);
                    segment.bezierPivot2.position = new Vector2Int(-segment.bezierPivot2.position.x + (project.globalPivot.position.x * 2), segment.bezierPivot2.position.y);
                }

                // recalculate the pivot position of the shape.
                shape.CalculatePivotPosition();
            }
        }

        /// <summary>
        /// Called when the zoom in button is pressed. Will zoom in the grid.
        /// </summary>
        private void OnZoomIn()
        {
            switch (gridScale)
            {
                case 2: gridScale = 4; break;
                case 4: gridScale = 8; break;
                case 8: gridScale = 16; break;
                case 16: gridScale = 32; break;
                case 32: gridScale = 64; break;
                case 64: gridScale = 64; break;
                default: gridScale = 16; break;
            }
        }

        /// <summary>
        /// Called when the zoom out button is pressed. Will zoom out the grid.
        /// </summary>
        private void OnZoomOut()
        {
            switch (gridScale)
            {
                case 2 : gridScale = 2 ; break;
                case 4 : gridScale = 2 ; break;
                case 8 : gridScale = 4 ; break;
                case 16: gridScale = 8 ; break;
                case 32: gridScale = 16; break;
                case 64: gridScale = 32; break;
                default: gridScale = 16; break;
            }
        }

        /// <summary>
        /// Called when the create shape button is pressed. Will add a new shape.
        /// </summary>
        private void OnShapeCreate()
        {
            project.shapes.Add(new Shape());
        }

        /// <summary>
        /// Called when the insert segment button is pressed. Will split all selected segments.
        /// </summary>
        private void OnSegmentInsert()
        {
            foreach (Segment segment in selectedSegments)
            {
                Segment next = GetNextSegment(segment);
                int distance = Mathf.RoundToInt(Vector2Int.Distance(segment.position, next.position));
                if (distance < 2) continue; // too short to split segment.
                // calculate split position.
                Vector2 split = Vector2.Lerp(segment.position, next.position, 0.5f);
                // insert new segment at split position.
                Shape parent = GetShapeOfSegment(segment);
                parent.segments.Insert(parent.segments.IndexOf(next), new Segment(Mathf.RoundToInt(split.x), Mathf.RoundToInt(split.y)));

                // recalculate the pivot position of the shape.
                parent.CalculatePivotPosition();
            }
        }

        /// <summary>
        /// Called when the delete button is pressed. Will delete all selected segments and shapes.
        /// </summary>
        private void OnDelete()
        {
            // prevent the user from deleting too much.
            foreach (Shape shape in project.shapes)
            {
                if (shape.segments.Count - selectedSegments.Count() < 3)
                {
                    EditorUtility.DisplayDialog("2D Shape Editor", "A polygon must have at least 3 segments!", "Okay");
                    return;
                }
            }
            // remove all selected segments.
            foreach (Segment segment in selectedSegments)
            {
                Shape parent = GetShapeOfSegment(segment);
                parent.segments.Remove(segment);

                // recalculate the pivot position of the shape.
                parent.CalculatePivotPosition();
            }
            // remove all selected shapes.
            foreach (Shape shape in project.shapes.ToArray()) // use .ToArray() to iterate a clone.
            {
                if (IsObjectSelected(shape.pivot))
                {
                    project.shapes.Remove(shape);
                    selectedObjects.Remove(shape.pivot);
                }
            }
            ClearSelectionOf<Segment>();
        }

        /// <summary>
        /// Called when the linear segment button is pressed. Will set all selected segments to be linear.
        /// </summary>
        private void OnSegmentLinear()
        {
            foreach (Shape shape in project.shapes)
            {
                foreach (Segment segment in shape.segments)
                {
                    if (segment.type == SegmentType.Linear) continue;

                    // the segment or any of its bezier pivots can be used to mark it as linear.
                    if (IsObjectSelected(segment) || IsObjectSelected(segment.bezierPivot1) || IsObjectSelected(segment.bezierPivot2))
                    {
                        segment.type = SegmentType.Linear;
                    }
                }
            }
        }

        /// <summary>
        /// Called when the linear segment button is pressed. Will set all selected segments to be bezier.
        /// </summary>
        private void OnSegmentBezier()
        {
            foreach (Segment segment in selectedSegments)
            {
                // don't affect existing bezier segments.
                if (segment.type == SegmentType.Bezier) continue;
                segment.type = SegmentType.Bezier;

                // calculate a user friendly initial position.
                Segment next = GetNextSegment(segment);
                // calculate split positions.
                Vector2 first = Vector2.Lerp(segment.position, next.position, 0.25f);
                Vector2 second = Vector2.Lerp(segment.position, next.position, 0.75f);
                // set the bezier pivots to two positions on the segment.
                segment.bezierPivot1.position = new Vector2Int(Mathf.RoundToInt(first.x), Mathf.RoundToInt(first.y));
                segment.bezierPivot2.position = new Vector2Int(Mathf.RoundToInt(second.x), Mathf.RoundToInt(second.y));
            }
        }

        /// <summary>
        /// Called when the segment detail menu is pressed. Will let the user pick any selected segment's bezier's detail level.
        /// </summary>
        private void OnSegmentBezierDetail()
        {
            // let the user choose the amount of bezier curve detail.
            ShowCenteredPopupWindowContent(new ShapeEditorWindowPopup(ShapeEditorWindowPopup.PopupMode.BezierDetailLevel, project, (self) => {
                foreach (Shape shape in project.shapes)
                {
                    foreach (Segment segment in shape.segments)
                    {
                        if (segment.type == SegmentType.Linear) continue;
                        // the segment or any of its bezier pivots can be used to select it.
                        if (IsObjectSelected(segment) || IsObjectSelected(segment.bezierPivot1) || IsObjectSelected(segment.bezierPivot2))
                            segment.bezierDetail = self.bezierDetailLevel_Detail;
                    }
                }

                // show the changes.
                Repaint();
            }));
        }

        /// <summary>
        /// Called when the create polygon button is pressed.
        /// </summary>
        private void OnCreatePolygon()
        {
            // let the user choose the creation parameters.
            ShowCenteredPopupWindowContent(new ShapeEditorWindowPopup(ShapeEditorWindowPopup.PopupMode.CreatePolygon, project, (self) => {
                // create the polygon.
                Selection.activeGameObject.GetComponent<ShapeEditorBrush>().CreatePolygon(project);
            }));
        }

        /// <summary>
        /// Called when the extrude revolved button is pressed.
        /// </summary>
        private void OnExtrudeRevolve()
        {
            EditorUtility.DisplayDialog("2D Shape Editor", "This functionality has not been implemented yet.", "But!!");
        }

        /// <summary>
        /// Called when the extrude shape button is pressed.
        /// </summary>
        private void OnExtrudeShape()
        {
            // let the user choose the extrude parameters.
            ShowCenteredPopupWindowContent(new ShapeEditorWindowPopup(ShapeEditorWindowPopup.PopupMode.ExtrudeShape, project, (self) => {
                // extrude the shape.
                Selection.activeGameObject.GetComponent<ShapeEditorBrush>().ExtrudeShape(project);
            }));
        }

        /// <summary>
        /// Called when the extrude point button is pressed.
        /// </summary>
        private void OnExtrudePoint()
        {
            // let the user choose the extrude parameters.
            ShowCenteredPopupWindowContent(new ShapeEditorWindowPopup(ShapeEditorWindowPopup.PopupMode.ExtrudePoint, project, (self) => {
                // extrude the shape to a point.
                Selection.activeGameObject.GetComponent<ShapeEditorBrush>().ExtrudePoint(project);
            }));
        }

        /// <summary>
        /// Called when the extrude bevelled button is pressed.
        /// </summary>
        private void OnExtrudeBevel()
        {
            EditorUtility.DisplayDialog("2D Shape Editor", "This functionality has not been implemented yet.", "But!!");
        }

        private Rect GetViewportRect()
        {
            Rect viewportRect = Screen.safeArea;
            viewportRect.y += 18;
            viewportRect.height -= 40;
            return viewportRect;
        }
        
        private void GlDrawLine(float thickness, float x1, float y1, float x2, float y2)
        {
            var point1 = new Vector2(x1, y1);
            var point2 = new Vector2(x2, y2);

            Vector2 startPoint = Vector2.zero;
            Vector2 endPoint = Vector2.zero;

            var diffx = Mathf.Abs(point1.x - point2.x);
            var diffy = Mathf.Abs(point1.y - point2.y);

            if (diffx > diffy)
            {
                if (point1.x <= point2.x)
                {
                    startPoint = point1;
                    endPoint = point2;
                }
                else
                {
                    startPoint = point2;
                    endPoint = point1;
                }
            }
            else
            {
                if (point1.y <= point2.y)
                {
                    startPoint = point1;
                    endPoint = point2;
                }
                else
                {
                    startPoint = point2;
                    endPoint = point1;
                }
            }

            var angle = Mathf.Atan2(endPoint.y - startPoint.y, endPoint.x - startPoint.x);
            var perp = angle + Mathf.PI * 0.5f;

            var p1 = Vector3.zero;
            var p2 = Vector3.zero;
            var p3 = Vector3.zero;
            var p4 = Vector3.zero;

            var cosAngle = Mathf.Cos(angle);
            var cosPerp = Mathf.Cos(perp);
            var sinAngle = Mathf.Sin(angle);
            var sinPerp = Mathf.Sin(perp);

            var distance = Vector2.Distance(startPoint, endPoint);

            p1.x = startPoint.x - (thickness * 0.5f) * cosPerp;
            p1.y = startPoint.y - (thickness * 0.5f) * sinPerp;

            p2.x = startPoint.x + (thickness * 0.5f) * cosPerp;
            p2.y = startPoint.y + (thickness * 0.5f) * sinPerp;

            p3.x = p2.x + distance * cosAngle;
            p3.y = p2.y + distance * sinAngle;

            p4.x = p1.x + distance * cosAngle;
            p4.y = p1.y + distance * sinAngle;

            GL.Vertex3(p1.x, p1.y, 0);
            GL.Vertex3(p2.x, p2.y, 0);
            GL.Vertex3(p3.x, p3.y, 0);
            GL.Vertex3(p4.x, p4.y, 0);
        }

        private void GlDrawBezier(float thickness, Vector2 start, Vector2 p1, Vector2 p2, Vector2 end, int detail)
        {
            Vector3 lineStart = Bezier.GetPoint(start, p1, p2, end, 0f);
            for (int i = 1; i <= detail; i++)
            {
                Vector3 lineEnd = Bezier.GetPoint(start, p1, p2, end, i / (float)detail);
                GlDrawLine(thickness, lineStart.x, lineStart.y, lineEnd.x, lineEnd.y);
                lineStart = lineEnd;
            }
        }

        private void GlDrawRectangle(float x, float y, float w, float h)
        {
            w += x;
            h += y;
            GL.Vertex3(x, y, 0);
            GL.Vertex3(x, h, 0);
            GL.Vertex3(w, h, 0);
            GL.Vertex3(w, y, 0);
        }

        /// <summary>
        /// Converts a point on the screen to the point on the grid.
        /// </summary>
        /// <param name="point">The point to convert.</param>
        /// <returns>The point on the grid.</returns>
        private Vector2Int ScreenPointToGrid(Vector2 point)
        {
            Vector3 result = (point / gridScale) - (viewportScroll / gridScale);
            return new Vector2Int(Mathf.FloorToInt(result.x + 0.5f), Mathf.FloorToInt(result.y + 0.5f));
        }

        /// <summary>
        /// Converts a point on the grid to the point on the screen.
        /// </summary>
        /// <param name="point">The point to convert.</param>
        /// <returns>The point on the screen.</returns>
        private Vector2 GridPointToScreen(Vector2Int point)
        {
            return (point * (int)gridScale) + viewportScroll;
        }

        /// <summary>
        /// Shows the a popup in the center of the editor window.
        /// </summary>
        /// <param name="popup">The popup to show.</param>
        private void ShowCenteredPopupWindowContent(PopupWindowContent popup)
        {
            Vector2 size = popup.GetWindowSize();
            PopupWindow.Show(new Rect((Screen.safeArea.width / 2.0f) - (size.x / 2.0f), (Screen.safeArea.height / 2.0f) - (size.y / 2.0f), 0, 0), popup);
        }

        /// <summary>
        /// Called when the editor selection changes.
        /// </summary>
        private void OnSelectionChange()
        {
            // we have to repaint in case the user selects a shape editor brush.
            Repaint();
        }

        private MethodInfo isDockedMethod = null;

        /// <summary>
        /// Gets a value indicating whether this window is docked.
        /// </summary>
        /// <value><c>true</c> if this window is docked; otherwise, <c>false</c>.</value>
        private bool isDocked
        {
            get
            {
                if (isDockedMethod == null)
                    isDockedMethod = typeof(EditorWindow).GetProperty("docked", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).GetGetMethod(true);
                return (bool)isDockedMethod.Invoke(this, null);
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////
        // PUBLIC API                                                                            //
        ///////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Creates a copy of the specified project and loads it into the 2D Shape Editor.
        /// </summary>
        /// <param name="project">The project to make a copy of and load into the 2D Shape Editor.</param>
        public void LoadProject(Project project)
        {
            // load the project into the editor.
            this.project = project.Clone();
            // update the viewport so that the user can see the changes.
            Repaint();
        }
    }
}

#endif