/*

This file is part of the iText (R) project.
Copyright (c) 1998-2017 iText Group NV
Authors: Bruno Lowagie, Paulo Soares, et al.

This program is free software; you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License version 3
as published by the Free Software Foundation with the addition of the
following permission added to Section 15 as permitted in Section 7(a):
FOR ANY PART OF THE COVERED WORK IN WHICH THE COPYRIGHT IS OWNED BY
ITEXT GROUP. ITEXT GROUP DISCLAIMS THE WARRANTY OF NON INFRINGEMENT
OF THIRD PARTY RIGHTS

This program is distributed in the hope that it will be useful, but
WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
or FITNESS FOR A PARTICULAR PURPOSE.
See the GNU Affero General Public License for more details.
You should have received a copy of the GNU Affero General Public License
along with this program; if not, see http://www.gnu.org/licenses or write to
the Free Software Foundation, Inc., 51 Franklin Street, Fifth Floor,
Boston, MA, 02110-1301 USA, or download the license from the following URL:
http://itextpdf.com/terms-of-use/

The interactive user interfaces in modified source and object code versions
of this program must display Appropriate Legal Notices, as required under
Section 5 of the GNU Affero General Public License.

In accordance with Section 7(b) of the GNU Affero General Public License,
a covered work must retain the producer line in every PDF that is created
or manipulated using iText.

You can be released from the requirements of the license by purchasing
a commercial license. Buying such a license is mandatory as soon as you
develop commercial activities involving the iText software without
disclosing the source code of your own applications.
These activities include: offering paid services to customers as an ASP,
serving PDFs on the fly in a web application, shipping iText with a closed
source product.

For more information, please contact iText Software Corp. at this
address: sales@itextpdf.com
*/
using System;
using System.Collections.Generic;
using System.Text;
using iText.IO.Log;
using iText.IO.Util;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Action;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Extgstate;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Font;
using iText.Layout.Layout;
using iText.Layout.Minmaxwidth;
using iText.Layout.Properties;

namespace iText.Layout.Renderer {
    /// <summary>
    /// Defines the most common properties and behavior that are shared by most
    /// <see cref="IRenderer"/>
    /// implementations. All default Renderers are subclasses of
    /// this default implementation.
    /// </summary>
    public abstract class AbstractRenderer : IRenderer {
        /// <summary>
        /// The maximum difference between
        /// <see cref="iText.Kernel.Geom.Rectangle"/>
        /// coordinates to consider rectangles equal
        /// </summary>
        public const float EPS = 1e-4f;

        /// <summary>The infinity value which is used while layouting</summary>
        public const float INF = 1e6f;

        protected internal IList<IRenderer> childRenderers = new List<IRenderer>();

        protected internal IList<IRenderer> positionedRenderers = new List<IRenderer>();

        protected internal IPropertyContainer modelElement;

        protected internal bool flushed = false;

        protected internal LayoutArea occupiedArea;

        protected internal IRenderer parent;

        protected internal IDictionary<int, Object> properties = new Dictionary<int, Object>();

        protected internal bool isLastRendererForModelElement = true;

        /// <summary>Creates a renderer.</summary>
        protected internal AbstractRenderer() {
        }

        /// <summary>Creates a renderer for the specified layout element.</summary>
        /// <param name="modelElement">the layout element that will be drawn by this renderer</param>
        protected internal AbstractRenderer(IElement modelElement) {
            // TODO linkedList?
            this.modelElement = modelElement;
        }

        protected internal AbstractRenderer(iText.Layout.Renderer.AbstractRenderer other) {
            this.childRenderers = other.childRenderers;
            this.positionedRenderers = other.positionedRenderers;
            this.modelElement = other.modelElement;
            this.flushed = other.flushed;
            this.occupiedArea = other.occupiedArea != null ? other.occupiedArea.Clone() : null;
            this.parent = other.parent;
            this.properties.AddAll(other.properties);
            this.isLastRendererForModelElement = other.isLastRendererForModelElement;
        }

        /// <summary><inheritDoc/></summary>
        public virtual void AddChild(IRenderer renderer) {
            // https://www.webkit.org/blog/116/webcore-rendering-iii-layout-basics
            // "The rules can be summarized as follows:"...
            int? positioning = renderer.GetProperty<int?>(Property.POSITION);
            if (positioning == null || positioning == LayoutPosition.RELATIVE || positioning == LayoutPosition.STATIC) {
                childRenderers.Add(renderer);
            }
            else {
                if (positioning == LayoutPosition.FIXED) {
                    iText.Layout.Renderer.AbstractRenderer root = this;
                    while (root.parent is iText.Layout.Renderer.AbstractRenderer) {
                        root = (iText.Layout.Renderer.AbstractRenderer)root.parent;
                    }
                    if (root == this) {
                        positionedRenderers.Add(renderer);
                    }
                    else {
                        root.AddChild(renderer);
                    }
                }
                else {
                    if (positioning == LayoutPosition.ABSOLUTE) {
                        // For position=absolute, if none of the top, bottom, left, right properties are provided,
                        // the content should be displayed in the flow of the current content, not overlapping it.
                        // The behavior is just if it would be statically positioned except it does not affect other elements
                        iText.Layout.Renderer.AbstractRenderer positionedParent = this;
                        bool noPositionInfo = iText.Layout.Renderer.AbstractRenderer.NoAbsolutePositionInfo(renderer);
                        while (!positionedParent.IsPositioned() && !noPositionInfo) {
                            IRenderer parent = positionedParent.parent;
                            if (parent is iText.Layout.Renderer.AbstractRenderer) {
                                positionedParent = (iText.Layout.Renderer.AbstractRenderer)parent;
                            }
                            else {
                                break;
                            }
                        }
                        if (positionedParent == this) {
                            positionedRenderers.Add(renderer);
                        }
                        else {
                            positionedParent.AddChild(renderer);
                        }
                    }
                }
            }
            // Fetch positioned renderers from non-positioned child because they might be stuck there because child's parent was null previously
            if (renderer is iText.Layout.Renderer.AbstractRenderer && !((iText.Layout.Renderer.AbstractRenderer)renderer
                ).IsPositioned() && ((iText.Layout.Renderer.AbstractRenderer)renderer).positionedRenderers.Count > 0) {
                // For position=absolute, if none of the top, bottom, left, right properties are provided,
                // the content should be displayed in the flow of the current content, not overlapping it.
                // The behavior is just if it would be statically positioned except it does not affect other elements
                int pos = 0;
                IList<IRenderer> childPositionedRenderers = ((iText.Layout.Renderer.AbstractRenderer)renderer).positionedRenderers;
                while (pos < childPositionedRenderers.Count) {
                    if (iText.Layout.Renderer.AbstractRenderer.NoAbsolutePositionInfo(childPositionedRenderers[pos])) {
                        pos++;
                    }
                    else {
                        positionedRenderers.Add(childPositionedRenderers[pos]);
                        childPositionedRenderers.JRemoveAt(pos);
                    }
                }
            }
        }

        /// <summary><inheritDoc/></summary>
        public virtual IPropertyContainer GetModelElement() {
            return modelElement;
        }

        /// <summary><inheritDoc/></summary>
        public virtual IList<IRenderer> GetChildRenderers() {
            return childRenderers;
        }

        /// <summary><inheritDoc/></summary>
        public virtual bool HasProperty(int property) {
            return HasOwnProperty(property) || (modelElement != null && modelElement.HasProperty(property)) || (parent
                 != null && Property.IsPropertyInherited(property) && parent.HasProperty(property));
        }

        /// <summary><inheritDoc/></summary>
        public virtual bool HasOwnProperty(int property) {
            return properties.ContainsKey(property);
        }

        /// <summary>
        /// Checks if this renderer or its model element have the specified property,
        /// i.e.
        /// </summary>
        /// <remarks>
        /// Checks if this renderer or its model element have the specified property,
        /// i.e. if it was set to this very element or its very model element earlier.
        /// </remarks>
        /// <param name="property">the property to be checked</param>
        /// <returns>
        /// 
        /// <see langword="true"/>
        /// if this instance or its model element have given own property,
        /// <see langword="false"/>
        /// otherwise
        /// </returns>
        public virtual bool HasOwnOrModelProperty(int property) {
            return HasOwnOrModelProperty(this, property);
        }

        /// <summary><inheritDoc/></summary>
        public virtual void DeleteOwnProperty(int property) {
            properties.JRemove(property);
        }

        /// <summary>
        /// Deletes property from this very renderer, or in case the property is specified on its model element, the
        /// property of the model element is deleted
        /// </summary>
        /// <param name="property">the property key to be deleted</param>
        public virtual void DeleteProperty(int property) {
            if (properties.ContainsKey(property)) {
                properties.JRemove(property);
            }
            else {
                if (modelElement != null) {
                    modelElement.DeleteOwnProperty(property);
                }
            }
        }

        /// <summary><inheritDoc/></summary>
        public virtual T1 GetProperty<T1>(int key) {
            Object property;
            if ((property = properties.Get(key)) != null || properties.ContainsKey(key)) {
                return (T1)property;
            }
            if (modelElement != null && ((property = modelElement.GetProperty<T1>(key)) != null || modelElement.HasProperty
                (key))) {
                return (T1)property;
            }
            // TODO in some situations we will want to check inheritance with additional info, such as parent and descendant.
            if (parent != null && Property.IsPropertyInherited(key) && (property = parent.GetProperty<T1>(key)) != null
                ) {
                return (T1)property;
            }
            property = this.GetDefaultProperty<T1>(key);
            if (property != null) {
                return (T1)property;
            }
            return modelElement != null ? modelElement.GetDefaultProperty<T1>(key) : (T1)(Object)null;
        }

        /// <summary><inheritDoc/></summary>
        public virtual T1 GetOwnProperty<T1>(int property) {
            return (T1)properties.Get(property);
        }

        /// <summary><inheritDoc/></summary>
        public virtual T1 GetProperty<T1>(int property, T1 defaultValue) {
            T1 result = this.GetProperty<T1>(property);
            return result != null ? result : defaultValue;
        }

        /// <summary><inheritDoc/></summary>
        public virtual void SetProperty(int property, Object value) {
            properties.Put(property, value);
        }

        /// <summary><inheritDoc/></summary>
        public virtual T1 GetDefaultProperty<T1>(int property) {
            return (T1)(Object)null;
        }

        /// <summary>Returns a property with a certain key, as a font object.</summary>
        /// <param name="property">
        /// an
        /// <see cref="iText.Layout.Properties.Property">enum value</see>
        /// </param>
        /// <returns>
        /// a
        /// <see cref="iText.Kernel.Font.PdfFont"/>
        /// </returns>
        public virtual PdfFont GetPropertyAsFont(int property) {
            return this.GetProperty<PdfFont>(property);
        }

        /// <summary>Returns a property with a certain key, as a color.</summary>
        /// <param name="property">
        /// an
        /// <see cref="iText.Layout.Properties.Property">enum value</see>
        /// </param>
        /// <returns>
        /// a
        /// <see cref="iText.Kernel.Colors.Color"/>
        /// </returns>
        public virtual Color GetPropertyAsColor(int property) {
            return this.GetProperty<Color>(property);
        }

        /// <summary>
        /// Returns a property with a certain key, as a
        /// <see cref="iText.Layout.Properties.TransparentColor"/>
        /// .
        /// </summary>
        /// <param name="property">
        /// an
        /// <see cref="iText.Layout.Properties.Property">enum value</see>
        /// </param>
        /// <returns>
        /// a
        /// <see cref="iText.Layout.Properties.TransparentColor"/>
        /// </returns>
        public virtual TransparentColor GetPropertyAsTransparentColor(int property) {
            return this.GetProperty<TransparentColor>(property);
        }

        /// <summary>Returns a property with a certain key, as a floating point value.</summary>
        /// <param name="property">
        /// an
        /// <see cref="iText.Layout.Properties.Property">enum value</see>
        /// </param>
        /// <returns>
        /// a
        /// <see cref="float?"/>
        /// </returns>
        public virtual float? GetPropertyAsFloat(int property) {
            return NumberUtil.AsFloat(this.GetProperty<Object>(property));
        }

        /// <summary>Returns a property with a certain key, as a floating point value.</summary>
        /// <param name="property">
        /// an
        /// <see cref="iText.Layout.Properties.Property">enum value</see>
        /// </param>
        /// <param name="defaultValue">default value to be returned if property is not found</param>
        /// <returns>
        /// a
        /// <see cref="float?"/>
        /// </returns>
        public virtual float? GetPropertyAsFloat(int property, float? defaultValue) {
            return NumberUtil.AsFloat(this.GetProperty<Object>(property, defaultValue));
        }

        /// <summary>Returns a property with a certain key, as a boolean value.</summary>
        /// <param name="property">
        /// an
        /// <see cref="iText.Layout.Properties.Property">enum value</see>
        /// </param>
        /// <returns>
        /// a
        /// <see cref="bool?"/>
        /// </returns>
        public virtual bool? GetPropertyAsBoolean(int property) {
            return this.GetProperty<bool?>(property);
        }

        /// <summary>Returns a property with a certain key, as an integer value.</summary>
        /// <param name="property">
        /// an
        /// <see cref="iText.Layout.Properties.Property">enum value</see>
        /// </param>
        /// <returns>
        /// a
        /// <see cref="int?"/>
        /// </returns>
        public virtual int? GetPropertyAsInteger(int property) {
            return NumberUtil.AsInteger(this.GetProperty<Object>(property));
        }

        /// <summary>Returns a string representation of the renderer.</summary>
        /// <returns>
        /// a
        /// <see cref="System.String"/>
        /// </returns>
        /// <seealso cref="System.Object.ToString()"/>
        public override String ToString() {
            StringBuilder sb = new StringBuilder();
            foreach (IRenderer renderer in childRenderers) {
                sb.Append(renderer.ToString());
            }
            return sb.ToString();
        }

        /// <summary><inheritDoc/></summary>
        public virtual LayoutArea GetOccupiedArea() {
            return occupiedArea;
        }

        /// <summary><inheritDoc/></summary>
        public virtual void Draw(DrawContext drawContext) {
            ApplyDestinationsAndAnnotation(drawContext);
            bool relativePosition = IsRelativePosition();
            if (relativePosition) {
                ApplyRelativePositioningTranslation(false);
            }
            BeginElementOpacityApplying(drawContext);
            DrawBackground(drawContext);
            DrawBorder(drawContext);
            DrawChildren(drawContext);
            DrawPositionedChildren(drawContext);
            EndElementOpacityApplying(drawContext);
            if (relativePosition) {
                ApplyRelativePositioningTranslation(true);
            }
            flushed = true;
        }

        protected internal virtual void BeginElementOpacityApplying(DrawContext drawContext) {
            float? opacity = this.GetPropertyAsFloat(Property.OPACITY);
            if (opacity != null && opacity < 1f) {
                PdfExtGState extGState = new PdfExtGState();
                extGState.SetStrokeOpacity((float)opacity).SetFillOpacity((float)opacity);
                drawContext.GetCanvas().SaveState().SetExtGState(extGState);
            }
        }

        protected internal virtual void EndElementOpacityApplying(DrawContext drawContext) {
            float? opacity = this.GetPropertyAsFloat(Property.OPACITY);
            if (opacity != null && opacity < 1f) {
                drawContext.GetCanvas().RestoreState();
            }
        }

        /// <summary>
        /// Draws a background layer if it is defined by a key
        /// <see cref="iText.Layout.Properties.Property.BACKGROUND"/>
        /// in either the layout element or this
        /// <see cref="IRenderer"/>
        /// itself.
        /// </summary>
        /// <param name="drawContext">the context (canvas, document, etc) of this drawing operation.</param>
        public virtual void DrawBackground(DrawContext drawContext) {
            Background background = this.GetProperty<Background>(Property.BACKGROUND);
            BackgroundImage backgroundImage = this.GetProperty<BackgroundImage>(Property.BACKGROUND_IMAGE);
            if (background != null || backgroundImage != null) {
                Rectangle bBox = GetOccupiedAreaBBox();
                bool isTagged = drawContext.IsTaggingEnabled() && GetModelElement() is IAccessibleElement;
                if (isTagged) {
                    drawContext.GetCanvas().OpenTag(new CanvasArtifact());
                }
                Rectangle backgroundArea = ApplyMargins(bBox, false);
                if (backgroundArea.GetWidth() <= 0 || backgroundArea.GetHeight() <= 0) {
                    ILogger logger = LoggerFactory.GetLogger(typeof(iText.Layout.Renderer.AbstractRenderer));
                    logger.Warn(MessageFormatUtil.Format(iText.IO.LogMessageConstant.RECTANGLE_HAS_NEGATIVE_OR_ZERO_SIZES, "background"
                        ));
                }
                else {
                    bool backgroundAreaIsClipped = false;
                    if (background != null) {
                        backgroundAreaIsClipped = ClipBackgroundArea(drawContext, backgroundArea);
                        TransparentColor backgroundColor = new TransparentColor(background.GetColor(), background.GetOpacity());
                        drawContext.GetCanvas().SaveState().SetFillColor(backgroundColor.GetColor());
                        backgroundColor.ApplyFillTransparency(drawContext.GetCanvas());
                        drawContext.GetCanvas().Rectangle(backgroundArea.GetX() - background.GetExtraLeft(), backgroundArea.GetY()
                             - background.GetExtraBottom(), backgroundArea.GetWidth() + background.GetExtraLeft() + background.GetExtraRight
                            (), backgroundArea.GetHeight() + background.GetExtraTop() + background.GetExtraBottom()).Fill().RestoreState
                            ();
                    }
                    if (backgroundImage != null && backgroundImage.GetImage() != null) {
                        if (!backgroundAreaIsClipped) {
                            backgroundAreaIsClipped = ClipBackgroundArea(drawContext, backgroundArea);
                        }
                        ApplyBorderBox(backgroundArea, false);
                        Rectangle imageRectangle = new Rectangle(backgroundArea.GetX(), backgroundArea.GetTop() - backgroundImage.
                            GetImage().GetHeight(), backgroundImage.GetImage().GetWidth(), backgroundImage.GetImage().GetHeight());
                        if (imageRectangle.GetWidth() <= 0 || imageRectangle.GetHeight() <= 0) {
                            ILogger logger = LoggerFactory.GetLogger(typeof(iText.Layout.Renderer.AbstractRenderer));
                            logger.Warn(MessageFormatUtil.Format(iText.IO.LogMessageConstant.RECTANGLE_HAS_NEGATIVE_OR_ZERO_SIZES, "background-image"
                                ));
                        }
                        else {
                            ApplyBorderBox(backgroundArea, true);
                            drawContext.GetCanvas().SaveState().Rectangle(backgroundArea).Clip().NewPath();
                            float initialX = backgroundImage.IsRepeatX() ? imageRectangle.GetX() - imageRectangle.GetWidth() : imageRectangle
                                .GetX();
                            float initialY = backgroundImage.IsRepeatY() ? imageRectangle.GetTop() : imageRectangle.GetY();
                            imageRectangle.SetY(initialY);
                            do {
                                imageRectangle.SetX(initialX);
                                do {
                                    drawContext.GetCanvas().AddXObject(backgroundImage.GetImage(), imageRectangle);
                                    imageRectangle.MoveRight(imageRectangle.GetWidth());
                                }
                                while (backgroundImage.IsRepeatX() && imageRectangle.GetLeft() < backgroundArea.GetRight());
                                imageRectangle.MoveDown(imageRectangle.GetHeight());
                            }
                            while (backgroundImage.IsRepeatY() && imageRectangle.GetTop() > backgroundArea.GetBottom());
                            drawContext.GetCanvas().RestoreState();
                        }
                    }
                    if (backgroundAreaIsClipped) {
                        drawContext.GetCanvas().RestoreState();
                    }
                }
                if (isTagged) {
                    drawContext.GetCanvas().CloseTag();
                }
            }
        }

        protected internal virtual bool ClipBorderArea(DrawContext drawContext, Rectangle outerBorderBox) {
            double curv = 0.4477f;
            UnitValue borderRadius = this.GetProperty<UnitValue>(Property.BORDER_RADIUS);
            float radius = 0;
            if (null != borderRadius) {
                if (borderRadius.IsPercentValue()) {
                    ILogger logger = LoggerFactory.GetLogger(typeof(BlockRenderer));
                    logger.Error(MessageFormatUtil.Format(iText.IO.LogMessageConstant.PROPERTY_IN_PERCENTS_NOT_SUPPORTED, "border-radius"
                        ));
                }
                else {
                    radius = borderRadius.GetValue();
                }
            }
            if (0 != radius) {
                float top = outerBorderBox.GetTop();
                float right = outerBorderBox.GetRight();
                float bottom = outerBorderBox.GetBottom();
                float left = outerBorderBox.GetLeft();
                float verticalRadius = Math.Min(outerBorderBox.GetHeight() / 2, radius);
                float horizontalRadius = Math.Min(outerBorderBox.GetWidth() / 2, radius);
                // radius border bbox
                float x1 = right - horizontalRadius;
                float y1 = top - verticalRadius;
                float x2 = right - horizontalRadius;
                float y2 = bottom + verticalRadius;
                float x3 = left + horizontalRadius;
                float y3 = bottom + verticalRadius;
                float x4 = left + horizontalRadius;
                float y4 = top - verticalRadius;
                PdfCanvas canvas = drawContext.GetCanvas();
                canvas.SaveState();
                // right top corner
                canvas.MoveTo(left, top).LineTo(x1, top).CurveTo(x1 + horizontalRadius * curv, top, right, y1 + verticalRadius
                     * curv, right, y1).LineTo(right, bottom).LineTo(left, bottom).LineTo(left, top);
                canvas.Clip().NewPath();
                // right bottom corner
                canvas.MoveTo(right, top).LineTo(right, y2).CurveTo(right, y2 - verticalRadius * curv, x2 + horizontalRadius
                     * curv, bottom, x2, bottom).LineTo(left, bottom).LineTo(left, top).LineTo(right, top);
                canvas.Clip().NewPath();
                // left bottom corner
                canvas.MoveTo(right, bottom).LineTo(x3, bottom).CurveTo(x3 - horizontalRadius * curv, bottom, left, y3 - verticalRadius
                     * curv, left, y3).LineTo(left, top).LineTo(right, top).LineTo(right, bottom);
                canvas.Clip().NewPath();
                // left top corner
                canvas.MoveTo(left, bottom).LineTo(left, y4).CurveTo(left, y4 + verticalRadius * curv, x4 - horizontalRadius
                     * curv, top, x4, top).LineTo(right, top).LineTo(right, bottom).LineTo(left, bottom);
                canvas.Clip().NewPath();
                Border[] borders = GetBorders();
                float radiusTop = verticalRadius;
                float radiusRight = horizontalRadius;
                float radiusBottom = verticalRadius;
                float radiusLeft = horizontalRadius;
                float topBorderWidth = 0;
                float rightBorderWidth = 0;
                float bottomBorderWidth = 0;
                float leftBorderWidth = 0;
                if (borders[0] != null) {
                    topBorderWidth = borders[0].GetWidth();
                    top = top - borders[0].GetWidth();
                    if (y1 > top) {
                        y1 = top;
                        y4 = top;
                    }
                    radiusTop = Math.Max(0, radiusTop - borders[0].GetWidth());
                }
                if (borders[1] != null) {
                    rightBorderWidth = borders[1].GetWidth();
                    right = right - borders[1].GetWidth();
                    if (x1 > right) {
                        x1 = right;
                        x2 = right;
                    }
                    radiusRight = Math.Max(0, radiusRight - borders[1].GetWidth());
                }
                if (borders[2] != null) {
                    bottomBorderWidth = borders[2].GetWidth();
                    bottom = bottom + borders[2].GetWidth();
                    if (x3 < left) {
                        x3 = left;
                        x4 = left;
                    }
                    radiusBottom = Math.Max(0, radiusBottom - borders[2].GetWidth());
                }
                if (borders[3] != null) {
                    leftBorderWidth = borders[3].GetWidth();
                    left = left + borders[3].GetWidth();
                    radiusLeft = Math.Max(0, radiusLeft - borders[3].GetWidth());
                }
                canvas.MoveTo(x1, top).CurveTo(x1 + Math.Min(radiusTop, radiusRight) * curv, top, right, y1 + Math.Min(radiusTop
                    , radiusRight) * curv, right, y1).LineTo(right, y2).LineTo(x3, y2).LineTo(x3, top).LineTo(x1, top).LineTo
                    (x1, top + topBorderWidth).LineTo(left - leftBorderWidth, top + topBorderWidth).LineTo(left - leftBorderWidth
                    , bottom - bottomBorderWidth).LineTo(right + rightBorderWidth, bottom - bottomBorderWidth).LineTo(right
                     + rightBorderWidth, top + topBorderWidth).LineTo(x1, top + topBorderWidth);
                canvas.Clip().NewPath();
                canvas.MoveTo(right, y2).CurveTo(right, y2 - Math.Min(radiusRight, radiusBottom) * curv, x2 + Math.Min(radiusRight
                    , radiusBottom) * curv, bottom, x2, bottom).LineTo(x3, bottom).LineTo(x3, y4).LineTo(right, y4).LineTo
                    (right, y2).LineTo(right + rightBorderWidth, y2).LineTo(right + rightBorderWidth, top + topBorderWidth
                    ).LineTo(left - leftBorderWidth, top + topBorderWidth).LineTo(left - leftBorderWidth, bottom - bottomBorderWidth
                    ).LineTo(right + rightBorderWidth, bottom - bottomBorderWidth).LineTo(right + rightBorderWidth, y2);
                canvas.Clip().NewPath();
                canvas.MoveTo(x3, bottom).CurveTo(x3 - Math.Min(radiusBottom, radiusLeft) * curv, bottom, left, y3 - Math.
                    Min(radiusBottom, radiusLeft) * curv, left, y3).LineTo(left, y4).LineTo(x1, y4).LineTo(x1, bottom).LineTo
                    (x3, bottom).LineTo(x3, bottom - bottomBorderWidth).LineTo(right + rightBorderWidth, bottom - bottomBorderWidth
                    ).LineTo(right + rightBorderWidth, top + topBorderWidth).LineTo(left - leftBorderWidth, top + topBorderWidth
                    ).LineTo(left - leftBorderWidth, bottom - bottomBorderWidth).LineTo(x3, bottom - bottomBorderWidth);
                canvas.Clip().NewPath();
                canvas.MoveTo(left, y4).CurveTo(left, y4 + Math.Min(radiusLeft, radiusTop) * curv, x4 - Math.Min(radiusLeft
                    , radiusTop) * curv, top, x4, top).LineTo(x1, top).LineTo(x1, y2).LineTo(left, y2).LineTo(left, y4).LineTo
                    (left - leftBorderWidth, y4).LineTo(left - leftBorderWidth, bottom - bottomBorderWidth).LineTo(right +
                     rightBorderWidth, bottom - bottomBorderWidth).LineTo(right + rightBorderWidth, top + topBorderWidth).
                    LineTo(left - leftBorderWidth, top + topBorderWidth).LineTo(left - leftBorderWidth, y4);
                canvas.Clip().NewPath();
            }
            return 0 != radius;
        }

        protected internal virtual bool ClipBackgroundArea(DrawContext drawContext, Rectangle outerBorderBox) {
            double curv = 0.4477f;
            UnitValue borderRadius = this.GetProperty<UnitValue>(Property.BORDER_RADIUS);
            float radius = 0;
            if (null != borderRadius) {
                if (borderRadius.IsPercentValue()) {
                    ILogger logger = LoggerFactory.GetLogger(typeof(BlockRenderer));
                    logger.Error(MessageFormatUtil.Format(iText.IO.LogMessageConstant.PROPERTY_IN_PERCENTS_NOT_SUPPORTED, "border-radius"
                        ));
                }
                else {
                    radius = borderRadius.GetValue();
                }
            }
            if (0 != radius) {
                float top = outerBorderBox.GetTop();
                float right = outerBorderBox.GetRight();
                float bottom = outerBorderBox.GetBottom();
                float left = outerBorderBox.GetLeft();
                float verticalRadius = Math.Min(outerBorderBox.GetHeight() / 2, radius);
                float horizontalRadius = Math.Min(outerBorderBox.GetWidth() / 2, radius);
                // radius border bbox
                float x1 = right - horizontalRadius;
                float y1 = top - verticalRadius;
                float x2 = right - horizontalRadius;
                float y2 = bottom + verticalRadius;
                float x3 = left + horizontalRadius;
                float y3 = bottom + verticalRadius;
                float x4 = left + horizontalRadius;
                float y4 = top - verticalRadius;
                PdfCanvas canvas = drawContext.GetCanvas();
                canvas.SaveState();
                canvas.MoveTo(left, top).LineTo(x1, top).CurveTo(x1 + horizontalRadius * curv, top, right, y1 + verticalRadius
                     * curv, right, y1).LineTo(right, bottom).LineTo(left, bottom).LineTo(left, top);
                canvas.Clip().NewPath();
                canvas.MoveTo(right, top).LineTo(right, y2).CurveTo(right, y2 - verticalRadius * curv, x2 + horizontalRadius
                     * curv, bottom, x2, bottom).LineTo(left, bottom).LineTo(left, top).LineTo(right, top);
                canvas.Clip().NewPath();
                canvas.MoveTo(right, bottom).LineTo(x3, bottom).CurveTo(x3 - horizontalRadius * curv, bottom, left, y3 - verticalRadius
                     * curv, left, y3).LineTo(left, top).LineTo(right, top).LineTo(right, bottom);
                canvas.Clip().NewPath();
                canvas.MoveTo(left, bottom).LineTo(left, y4).CurveTo(left, y4 + verticalRadius * curv, x4 - horizontalRadius
                     * curv, top, x4, top).LineTo(right, top).LineTo(right, bottom).LineTo(left, bottom);
                canvas.Clip().NewPath();
            }
            return 0 != radius;
        }

        /// <summary>
        /// Performs the drawing operation for all
        /// <see cref="IRenderer">children</see>
        /// of this renderer.
        /// </summary>
        /// <param name="drawContext">the context (canvas, document, etc) of this drawing operation.</param>
        public virtual void DrawChildren(DrawContext drawContext) {
            IList<IRenderer> waitingRenderers = new List<IRenderer>();
            foreach (IRenderer child in childRenderers) {
                Transform transformProp = child.GetProperty<Transform>(Property.TRANSFORM);
                Border outlineProp = child.GetProperty<Border>(Property.OUTLINE);
                RootRenderer rootRenderer = GetRootRenderer();
                IList<IRenderer> waiting = (rootRenderer != null && !rootRenderer.waitingDrawingElements.Contains(child)) ? 
                    rootRenderer.waitingDrawingElements : waitingRenderers;
                ProcessWaitingDrawing(child, transformProp, outlineProp, waiting);
                if (!FloatingHelper.IsRendererFloating(child) && transformProp == null) {
                    child.Draw(drawContext);
                }
            }
            foreach (IRenderer waitingRenderer in waitingRenderers) {
                waitingRenderer.Draw(drawContext);
            }
        }

        internal static void ProcessWaitingDrawing(IRenderer child, Transform transformProp, Border outlineProp, IList
            <IRenderer> waitingDrawing) {
            if (FloatingHelper.IsRendererFloating(child) || transformProp != null) {
                waitingDrawing.Add(child);
            }
            if (outlineProp != null && child is iText.Layout.Renderer.AbstractRenderer) {
                iText.Layout.Renderer.AbstractRenderer abstractChild = (iText.Layout.Renderer.AbstractRenderer)child;
                if (abstractChild.IsRelativePosition()) {
                    abstractChild.ApplyRelativePositioningTranslation(false);
                }
                Div outlines = new Div();
                outlines.SetRole(null);
                if (transformProp != null) {
                    outlines.SetProperty(Property.TRANSFORM, transformProp);
                }
                outlines.SetProperty(Property.BORDER, outlineProp);
                float offset = outlines.GetProperty<Border>(Property.BORDER).GetWidth();
                if (abstractChild.GetPropertyAsFloat(Property.OUTLINE_OFFSET) != null) {
                    offset += (float)abstractChild.GetPropertyAsFloat(Property.OUTLINE_OFFSET);
                }
                DivRenderer div = new DivRenderer(outlines);
                Rectangle divOccupiedArea = abstractChild.ApplyMargins(abstractChild.occupiedArea.Clone().GetBBox(), false
                    ).MoveLeft(offset).MoveDown(offset);
                divOccupiedArea.SetWidth(divOccupiedArea.GetWidth() + 2 * offset).SetHeight(divOccupiedArea.GetHeight() + 
                    2 * offset);
                div.occupiedArea = new LayoutArea(abstractChild.GetOccupiedArea().GetPageNumber(), divOccupiedArea);
                float outlineWidth = div.GetProperty<Border>(Property.BORDER).GetWidth();
                if (divOccupiedArea.GetWidth() >= outlineWidth * 2 && divOccupiedArea.GetHeight() >= outlineWidth * 2) {
                    waitingDrawing.Add(div);
                }
                if (abstractChild.IsRelativePosition()) {
                    abstractChild.ApplyRelativePositioningTranslation(true);
                }
            }
        }

        /// <summary>
        /// Performs the drawing operation for the border of this renderer, if
        /// defined by any of the
        /// <see cref="iText.Layout.Properties.Property.BORDER"/>
        /// values in either the layout
        /// element or this
        /// <see cref="IRenderer"/>
        /// itself.
        /// </summary>
        /// <param name="drawContext">the context (canvas, document, etc) of this drawing operation.</param>
        public virtual void DrawBorder(DrawContext drawContext) {
            Border[] borders = GetBorders();
            bool gotBorders = false;
            foreach (Border border in borders) {
                gotBorders = gotBorders || border != null;
            }
            if (gotBorders) {
                float topWidth = borders[0] != null ? borders[0].GetWidth() : 0;
                float rightWidth = borders[1] != null ? borders[1].GetWidth() : 0;
                float bottomWidth = borders[2] != null ? borders[2].GetWidth() : 0;
                float leftWidth = borders[3] != null ? borders[3].GetWidth() : 0;
                Rectangle bBox = GetBorderAreaBBox();
                if (bBox.GetWidth() < 0 || bBox.GetHeight() < 0) {
                    ILogger logger = LoggerFactory.GetLogger(typeof(iText.Layout.Renderer.AbstractRenderer));
                    logger.Error(MessageFormatUtil.Format(iText.IO.LogMessageConstant.RECTANGLE_HAS_NEGATIVE_SIZE, "border"));
                    return;
                }
                float x1 = bBox.GetX();
                float y1 = bBox.GetY();
                float x2 = bBox.GetX() + bBox.GetWidth();
                float y2 = bBox.GetY() + bBox.GetHeight();
                bool isTagged = drawContext.IsTaggingEnabled() && GetModelElement() is IAccessibleElement;
                PdfCanvas canvas = drawContext.GetCanvas();
                if (isTagged) {
                    canvas.OpenTag(new CanvasArtifact());
                }
                bool isAreaClipped = ClipBorderArea(drawContext, ApplyMargins(occupiedArea.GetBBox().Clone(), GetMargins()
                    , false));
                UnitValue borderRadius = this.GetProperty<UnitValue>(Property.BORDER_RADIUS);
                float radius = 0;
                if (null != borderRadius) {
                    if (borderRadius.IsPercentValue()) {
                        ILogger logger = LoggerFactory.GetLogger(typeof(BlockRenderer));
                        logger.Error(MessageFormatUtil.Format(iText.IO.LogMessageConstant.PROPERTY_IN_PERCENTS_NOT_SUPPORTED, "border-radius"
                            ));
                    }
                    else {
                        radius = borderRadius.GetValue();
                    }
                }
                if (0 == radius) {
                    if (borders[0] != null) {
                        borders[0].Draw(canvas, x1, y2, x2, y2, Border.Side.TOP, leftWidth, rightWidth);
                    }
                    if (borders[1] != null) {
                        borders[1].Draw(canvas, x2, y2, x2, y1, Border.Side.RIGHT, topWidth, bottomWidth);
                    }
                    if (borders[2] != null) {
                        borders[2].Draw(canvas, x2, y1, x1, y1, Border.Side.BOTTOM, rightWidth, leftWidth);
                    }
                    if (borders[3] != null) {
                        borders[3].Draw(canvas, x1, y1, x1, y2, Border.Side.LEFT, bottomWidth, topWidth);
                    }
                }
                else {
                    if (borders[0] != null) {
                        borders[0].Draw(canvas, x1, y2, x2, y2, radius, Border.Side.TOP, leftWidth, rightWidth);
                    }
                    if (borders[1] != null) {
                        borders[1].Draw(canvas, x2, y2, x2, y1, radius, Border.Side.RIGHT, topWidth, bottomWidth);
                    }
                    if (borders[2] != null) {
                        borders[2].Draw(canvas, x2, y1, x1, y1, radius, Border.Side.BOTTOM, rightWidth, leftWidth);
                    }
                    if (borders[3] != null) {
                        borders[3].Draw(canvas, x1, y1, x1, y2, radius, Border.Side.LEFT, bottomWidth, topWidth);
                    }
                }
                if (isAreaClipped) {
                    drawContext.GetCanvas().RestoreState();
                }
                if (isTagged) {
                    canvas.CloseTag();
                }
            }
        }

        /// <summary>Indicates whether this renderer is flushed or not, i.e.</summary>
        /// <remarks>
        /// Indicates whether this renderer is flushed or not, i.e. if
        /// <see cref="Draw(DrawContext)"/>
        /// has already
        /// been called.
        /// </remarks>
        /// <returns>whether the renderer has been flushed</returns>
        /// <seealso cref="Draw(DrawContext)"/>
        public virtual bool IsFlushed() {
            return flushed;
        }

        /// <summary><inheritDoc/></summary>
        public virtual IRenderer SetParent(IRenderer parent) {
            this.parent = parent;
            return this;
        }

        /// <summary>
        /// Gets the parent of this
        /// <see cref="IRenderer"/>
        /// , if previously set by
        /// <see cref="SetParent(IRenderer)"/>
        /// </summary>
        /// <returns>parent of the renderer</returns>
        public virtual IRenderer GetParent() {
            return parent;
        }

        /// <summary><inheritDoc/></summary>
        public virtual void Move(float dxRight, float dyUp) {
            occupiedArea.GetBBox().MoveRight(dxRight);
            occupiedArea.GetBBox().MoveUp(dyUp);
            foreach (IRenderer childRenderer in childRenderers) {
                childRenderer.Move(dxRight, dyUp);
            }
            foreach (IRenderer childRenderer in positionedRenderers) {
                childRenderer.Move(dxRight, dyUp);
            }
        }

        /// <summary>
        /// Gets all rectangles that this
        /// <see cref="IRenderer"/>
        /// can draw upon in the given area.
        /// </summary>
        /// <param name="area">
        /// a physical area on the
        /// <see cref="DrawContext"/>
        /// </param>
        /// <returns>
        /// a list of
        /// <see cref="iText.Kernel.Geom.Rectangle">rectangles</see>
        /// </returns>
        public virtual IList<Rectangle> InitElementAreas(LayoutArea area) {
            return JavaCollectionsUtil.SingletonList(area.GetBBox());
        }

        /// <summary>
        /// Gets the bounding box that contains all content written to the
        /// <see cref="DrawContext"/>
        /// by this
        /// <see cref="IRenderer"/>
        /// .
        /// </summary>
        /// <returns>
        /// the smallest
        /// <see cref="iText.Kernel.Geom.Rectangle"/>
        /// that surrounds the content
        /// </returns>
        public virtual Rectangle GetOccupiedAreaBBox() {
            return occupiedArea.GetBBox().Clone();
        }

        /// <summary>Gets the border box of a renderer.</summary>
        /// <remarks>
        /// Gets the border box of a renderer.
        /// This is a box used to draw borders.
        /// </remarks>
        /// <returns>border box of a renderer</returns>
        public virtual Rectangle GetBorderAreaBBox() {
            Rectangle rect = GetOccupiedAreaBBox();
            ApplyMargins(rect, false);
            ApplyBorderBox(rect, false);
            return rect;
        }

        public virtual Rectangle GetInnerAreaBBox() {
            Rectangle rect = GetOccupiedAreaBBox();
            ApplyMargins(rect, false);
            ApplyBorderBox(rect, false);
            ApplyPaddings(rect, false);
            return rect;
        }

        protected internal virtual void ApplyDestinationsAndAnnotation(DrawContext drawContext) {
            ApplyDestination(drawContext.GetDocument());
            ApplyAction(drawContext.GetDocument());
            ApplyLinkAnnotation(drawContext.GetDocument());
        }

        internal static bool IsBorderBoxSizing(IRenderer renderer) {
            BoxSizingPropertyValue? boxSizing = renderer.GetProperty<BoxSizingPropertyValue?>(Property.BOX_SIZING);
            return boxSizing != null && boxSizing.Equals(BoxSizingPropertyValue.BORDER_BOX);
        }

        /// <summary>Retrieves element's fixed content box width, if it's set.</summary>
        /// <remarks>
        /// Retrieves element's fixed content box width, if it's set.
        /// Takes into account
        /// <see cref="iText.Layout.Properties.Property.BOX_SIZING"/>
        /// ,
        /// <see cref="iText.Layout.Properties.Property.MIN_WIDTH"/>
        /// ,
        /// and
        /// <see cref="iText.Layout.Properties.Property.MAX_WIDTH"/>
        /// properties.
        /// </remarks>
        /// <param name="parentBoxWidth">
        /// width of the parent element content box.
        /// If element has relative width, it will be
        /// calculated relatively to this parameter.
        /// </param>
        /// <returns>element's fixed content box width or null if it's not set.</returns>
        /// <seealso cref="HasAbsoluteUnitValue(int)"/>
        protected internal virtual float? RetrieveWidth(float parentBoxWidth) {
            float? minWidth = RetrieveUnitValue(parentBoxWidth, Property.MIN_WIDTH);
            float? maxWidth = RetrieveUnitValue(parentBoxWidth, Property.MAX_WIDTH);
            if (maxWidth != null && minWidth != null && minWidth > maxWidth) {
                maxWidth = minWidth;
            }
            float? width = RetrieveUnitValue(parentBoxWidth, Property.WIDTH);
            if (width != null) {
                if (maxWidth != null) {
                    width = width > maxWidth ? maxWidth : width;
                }
                if (minWidth != null) {
                    width = width < minWidth ? minWidth : width;
                }
            }
            else {
                if (maxWidth != null) {
                    width = maxWidth < parentBoxWidth ? maxWidth : null;
                }
            }
            if (width != null && IsBorderBoxSizing(this)) {
                width -= CalculatePaddingBorderWidth(this);
            }
            return width != null ? (float?)Math.Max(0, (float)width) : null;
        }

        /// <summary>Retrieves element's fixed content box max width, if it's set.</summary>
        /// <remarks>
        /// Retrieves element's fixed content box max width, if it's set.
        /// Takes into account
        /// <see cref="iText.Layout.Properties.Property.BOX_SIZING"/>
        /// and
        /// <see cref="iText.Layout.Properties.Property.MIN_WIDTH"/>
        /// properties.
        /// </remarks>
        /// <param name="parentBoxWidth">
        /// width of the parent element content box.
        /// If element has relative width, it will be
        /// calculated relatively to this parameter.
        /// </param>
        /// <returns>element's fixed content box max width or null if it's not set.</returns>
        /// <seealso cref="HasAbsoluteUnitValue(int)"/>
        protected internal virtual float? RetrieveMaxWidth(float parentBoxWidth) {
            float? maxWidth = RetrieveUnitValue(parentBoxWidth, Property.MAX_WIDTH);
            if (maxWidth != null) {
                float? minWidth = RetrieveUnitValue(parentBoxWidth, Property.MIN_WIDTH);
                if (minWidth != null && minWidth > maxWidth) {
                    maxWidth = minWidth;
                }
                if (IsBorderBoxSizing(this)) {
                    maxWidth -= CalculatePaddingBorderWidth(this);
                }
                return maxWidth > 0 ? maxWidth : 0;
            }
            else {
                return null;
            }
        }

        /// <summary>Retrieves element's fixed content box max width, if it's set.</summary>
        /// <remarks>
        /// Retrieves element's fixed content box max width, if it's set.
        /// Takes into account
        /// <see cref="iText.Layout.Properties.Property.BOX_SIZING"/>
        /// property value.
        /// </remarks>
        /// <param name="parentBoxWidth">
        /// width of the parent element content box.
        /// If element has relative width, it will be
        /// calculated relatively to this parameter.
        /// </param>
        /// <returns>element's fixed content box max width or null if it's not set.</returns>
        /// <seealso cref="HasAbsoluteUnitValue(int)"/>
        protected internal virtual float? RetrieveMinWidth(float parentBoxWidth) {
            float? minWidth = RetrieveUnitValue(parentBoxWidth, Property.MIN_WIDTH);
            if (minWidth != null) {
                if (IsBorderBoxSizing(this)) {
                    minWidth -= CalculatePaddingBorderWidth(this);
                }
                return minWidth > 0 ? minWidth : 0;
            }
            else {
                return null;
            }
        }

        /// <summary>Updates fixed content box width value for this renderer.</summary>
        /// <remarks>
        /// Updates fixed content box width value for this renderer.
        /// Takes into account
        /// <see cref="iText.Layout.Properties.Property.BOX_SIZING"/>
        /// property value.
        /// </remarks>
        /// <param name="updatedWidthValue">element's new fixed content box width.</param>
        internal virtual void UpdateWidth(UnitValue updatedWidthValue) {
            if (updatedWidthValue.IsPointValue() && IsBorderBoxSizing(this)) {
                updatedWidthValue.SetValue(updatedWidthValue.GetValue() + CalculatePaddingBorderWidth(this));
            }
            SetProperty(Property.WIDTH, updatedWidthValue);
        }

        /// <summary>Retrieves element's fixed content box height, if it's set.</summary>
        /// <remarks>
        /// Retrieves element's fixed content box height, if it's set.
        /// Takes into account
        /// <see cref="iText.Layout.Properties.Property.BOX_SIZING"/>
        /// property value.
        /// </remarks>
        /// <returns>element's fixed content box height or null if it's not set.</returns>
        protected internal virtual float? RetrieveHeight() {
            float? height = this.GetProperty<float?>(Property.HEIGHT);
            if (height != null && IsBorderBoxSizing(this)) {
                height = Math.Max(0, (float)height - CalculatePaddingBorderHeight(this));
            }
            return height;
        }

        /// <summary>Updates fixed content box height value for this renderer.</summary>
        /// <remarks>
        /// Updates fixed content box height value for this renderer.
        /// Takes into account
        /// <see cref="iText.Layout.Properties.Property.BOX_SIZING"/>
        /// property value.
        /// </remarks>
        /// <param name="updatedHeightValue">element's new fixed content box height, shall be not null.</param>
        internal virtual void UpdateHeight(float? updatedHeightValue) {
            if (IsBorderBoxSizing(this)) {
                updatedHeightValue += CalculatePaddingBorderHeight(this);
            }
            SetProperty(Property.HEIGHT, updatedHeightValue);
        }

        /// <summary>Retrieves element's content box max-height, if it's set.</summary>
        /// <remarks>
        /// Retrieves element's content box max-height, if it's set.
        /// Takes into account
        /// <see cref="iText.Layout.Properties.Property.BOX_SIZING"/>
        /// property value.
        /// </remarks>
        /// <returns>element's content box max-height or null if it's not set.</returns>
        protected internal virtual float? RetrieveMaxHeight() {
            float? maxHeight = this.GetProperty<float?>(Property.MAX_HEIGHT);
            if (maxHeight != null && IsBorderBoxSizing(this)) {
                maxHeight = Math.Max(0, (float)maxHeight - CalculatePaddingBorderHeight(this));
            }
            return maxHeight;
        }

        /// <summary>Updates content box max-height value for this renderer.</summary>
        /// <remarks>
        /// Updates content box max-height value for this renderer.
        /// Takes into account
        /// <see cref="iText.Layout.Properties.Property.BOX_SIZING"/>
        /// property value.
        /// </remarks>
        /// <param name="updatedMaxHeightValue">element's new content box max-height, shall be not null.</param>
        internal virtual void UpdateMaxHeight(float? updatedMaxHeightValue) {
            if (IsBorderBoxSizing(this)) {
                updatedMaxHeightValue += CalculatePaddingBorderHeight(this);
            }
            SetProperty(Property.MAX_HEIGHT, updatedMaxHeightValue);
        }

        /// <summary>Retrieves element's content box max-height, if it's set.</summary>
        /// <remarks>
        /// Retrieves element's content box max-height, if it's set.
        /// Takes into account
        /// <see cref="iText.Layout.Properties.Property.BOX_SIZING"/>
        /// property value.
        /// </remarks>
        /// <returns>element's content box min-height or null if it's not set.</returns>
        protected internal virtual float? RetrieveMinHeight() {
            float? minHeight = this.GetProperty<float?>(Property.MIN_HEIGHT);
            if (minHeight != null && IsBorderBoxSizing(this)) {
                minHeight = Math.Max(0, (float)minHeight - CalculatePaddingBorderHeight(this));
            }
            return minHeight;
        }

        /// <summary>Updates content box min-height value for this renderer.</summary>
        /// <remarks>
        /// Updates content box min-height value for this renderer.
        /// Takes into account
        /// <see cref="iText.Layout.Properties.Property.BOX_SIZING"/>
        /// property value.
        /// </remarks>
        /// <param name="updatedMinHeightValue">element's new content box min-height, shall be not null.</param>
        internal virtual void UpdateMinHeight(float? updatedMinHeightValue) {
            if (IsBorderBoxSizing(this)) {
                updatedMinHeightValue += CalculatePaddingBorderHeight(this);
            }
            SetProperty(Property.MIN_HEIGHT, updatedMinHeightValue);
        }

        protected internal virtual float? RetrieveUnitValue(float basePercentValue, int property) {
            UnitValue value = this.GetProperty<UnitValue>(property);
            if (value != null) {
                if (value.GetUnitType() == UnitValue.PERCENT) {
                    return value.GetValue() * basePercentValue / 100;
                }
                else {
                    System.Diagnostics.Debug.Assert(value.GetUnitType() == UnitValue.POINT);
                    return value.GetValue();
                }
            }
            else {
                return null;
            }
        }

        //TODO is behavior of copying all properties in split case common to all renderers?
        protected internal virtual IDictionary<int, Object> GetOwnProperties() {
            return properties;
        }

        protected internal virtual void AddAllProperties(IDictionary<int, Object> properties) {
            this.properties.AddAll(properties);
        }

        /// <summary>Gets the first yLine of the nested children recursively.</summary>
        /// <remarks>
        /// Gets the first yLine of the nested children recursively. E.g. for a list, this will be the yLine of the
        /// first item (if the first item is indeed a paragraph).
        /// NOTE: this method will no go further than the first child.
        /// Returns null if there is no text found.
        /// </remarks>
        protected internal virtual float? GetFirstYLineRecursively() {
            if (childRenderers.Count == 0) {
                return null;
            }
            return ((iText.Layout.Renderer.AbstractRenderer)childRenderers[0]).GetFirstYLineRecursively();
        }

        protected internal virtual float? GetLastYLineRecursively() {
            for (int i = childRenderers.Count - 1; i >= 0; i--) {
                IRenderer child = childRenderers[i];
                if (child is iText.Layout.Renderer.AbstractRenderer) {
                    float? lastYLine = ((iText.Layout.Renderer.AbstractRenderer)child).GetLastYLineRecursively();
                    if (lastYLine != null) {
                        return lastYLine;
                    }
                }
            }
            return null;
        }

        /// <summary>Applies margins of the renderer on the given rectangle</summary>
        /// <param name="rect">a rectangle margins will be applied on.</param>
        /// <param name="reverse">
        /// indicates whether margins will be applied
        /// inside (in case of false) or outside (in case of true) the rectangle.
        /// </param>
        /// <returns>
        /// a
        /// <see cref="iText.Kernel.Geom.Rectangle">border box</see>
        /// of the renderer
        /// </returns>
        /// <seealso cref="GetMargins()"/>
        protected internal virtual Rectangle ApplyMargins(Rectangle rect, bool reverse) {
            return this.ApplyMargins(rect, GetMargins(), reverse);
        }

        /// <summary>Applies given margins on the given rectangle</summary>
        /// <param name="rect">a rectangle margins will be applied on.</param>
        /// <param name="margins">the margins to be applied on the given rectangle</param>
        /// <param name="reverse">
        /// indicates whether margins will be applied
        /// inside (in case of false) or outside (in case of true) the rectangle.
        /// </param>
        /// <returns>
        /// a
        /// <see cref="iText.Kernel.Geom.Rectangle">border box</see>
        /// of the renderer
        /// </returns>
        protected internal virtual Rectangle ApplyMargins(Rectangle rect, float[] margins, bool reverse) {
            return rect.ApplyMargins<Rectangle>(margins[0], margins[1], margins[2], margins[3], reverse);
        }

        /// <summary>Returns margins of the renderer</summary>
        /// <returns>
        /// a
        /// <c>float[]</c>
        /// margins of the renderer
        /// </returns>
        protected internal virtual float[] GetMargins() {
            return GetMargins(this);
        }

        /// <summary>Returns paddings of the renderer</summary>
        /// <returns>
        /// a
        /// <c>float[]</c>
        /// paddings of the renderer
        /// </returns>
        protected internal virtual float[] GetPaddings() {
            return GetPaddings(this);
        }

        /// <summary>Applies paddings of the renderer on the given rectangle</summary>
        /// <param name="rect">a rectangle paddings will be applied on.</param>
        /// <param name="reverse">
        /// indicates whether paddings will be applied
        /// inside (in case of false) or outside (in case of false) the rectangle.
        /// </param>
        /// <returns>
        /// a
        /// <see cref="iText.Kernel.Geom.Rectangle">border box</see>
        /// of the renderer
        /// </returns>
        /// <seealso cref="GetPaddings()"/>
        protected internal virtual Rectangle ApplyPaddings(Rectangle rect, bool reverse) {
            return ApplyPaddings(rect, GetPaddings(), reverse);
        }

        /// <summary>Applies given paddings on the given rectangle</summary>
        /// <param name="rect">a rectangle paddings will be applied on.</param>
        /// <param name="paddings">the paddings to be applied on the given rectangle</param>
        /// <param name="reverse">
        /// indicates whether paddings will be applied
        /// inside (in case of false) or outside (in case of false) the rectangle.
        /// </param>
        /// <returns>
        /// a
        /// <see cref="iText.Kernel.Geom.Rectangle">border box</see>
        /// of the renderer
        /// </returns>
        protected internal virtual Rectangle ApplyPaddings(Rectangle rect, float[] paddings, bool reverse) {
            return rect.ApplyMargins<Rectangle>(paddings[0], paddings[1], paddings[2], paddings[3], reverse);
        }

        /// <summary>
        /// Applies the border box of the renderer on the given rectangle
        /// If the border of a certain side is null, the side will remain as it was.
        /// </summary>
        /// <param name="rect">a rectangle the border box will be applied on.</param>
        /// <param name="reverse">
        /// indicates whether the border box will be applied
        /// inside (in case of false) or outside (in case of false) the rectangle.
        /// </param>
        /// <returns>
        /// a
        /// <see cref="iText.Kernel.Geom.Rectangle">border box</see>
        /// of the renderer
        /// </returns>
        /// <seealso cref="GetBorders()"/>
        protected internal virtual Rectangle ApplyBorderBox(Rectangle rect, bool reverse) {
            Border[] borders = GetBorders();
            return ApplyBorderBox(rect, borders, reverse);
        }

        /// <summary>Applies the given border box (borders) on the given rectangle</summary>
        /// <param name="rect">a rectangle paddings will be applied on.</param>
        /// <param name="borders">
        /// the
        /// <see cref="iText.Layout.Borders.Border">borders</see>
        /// to be applied on the given rectangle
        /// </param>
        /// <param name="reverse">
        /// indicates whether the border box will be applied
        /// inside (in case of false) or outside (in case of false) the rectangle.
        /// </param>
        /// <returns>
        /// a
        /// <see cref="iText.Kernel.Geom.Rectangle">border box</see>
        /// of the renderer
        /// </returns>
        protected internal virtual Rectangle ApplyBorderBox(Rectangle rect, Border[] borders, bool reverse) {
            float topWidth = borders[0] != null ? borders[0].GetWidth() : 0;
            float rightWidth = borders[1] != null ? borders[1].GetWidth() : 0;
            float bottomWidth = borders[2] != null ? borders[2].GetWidth() : 0;
            float leftWidth = borders[3] != null ? borders[3].GetWidth() : 0;
            return rect.ApplyMargins<Rectangle>(topWidth, rightWidth, bottomWidth, leftWidth, reverse);
        }

        protected internal virtual void ApplyAbsolutePosition(Rectangle parentRect) {
            float? top = this.GetPropertyAsFloat(Property.TOP);
            float? bottom = this.GetPropertyAsFloat(Property.BOTTOM);
            float? left = this.GetPropertyAsFloat(Property.LEFT);
            float? right = this.GetPropertyAsFloat(Property.RIGHT);
            if (left == null && right == null && BaseDirection.RIGHT_TO_LEFT.Equals(this.GetProperty<BaseDirection?>(Property
                .BASE_DIRECTION))) {
                right = 0f;
            }
            if (top == null && bottom == null) {
                top = 0f;
            }
            try {
                if (right != null) {
                    Move(parentRect.GetRight() - (float)right - occupiedArea.GetBBox().GetRight(), 0);
                }
                if (left != null) {
                    Move(parentRect.GetLeft() + (float)left - occupiedArea.GetBBox().GetLeft(), 0);
                }
                if (top != null) {
                    Move(0, parentRect.GetTop() - (float)top - occupiedArea.GetBBox().GetTop());
                }
                if (bottom != null) {
                    Move(0, parentRect.GetBottom() + (float)bottom - occupiedArea.GetBBox().GetBottom());
                }
            }
            catch (Exception) {
                ILogger logger = LoggerFactory.GetLogger(typeof(iText.Layout.Renderer.AbstractRenderer));
                logger.Error(MessageFormatUtil.Format(iText.IO.LogMessageConstant.OCCUPIED_AREA_HAS_NOT_BEEN_INITIALIZED, 
                    "Absolute positioning might be applied incorrectly."));
            }
        }

        protected internal virtual void ApplyRelativePositioningTranslation(bool reverse) {
            float top = (float)this.GetPropertyAsFloat(Property.TOP, 0f);
            float bottom = (float)this.GetPropertyAsFloat(Property.BOTTOM, 0f);
            float left = (float)this.GetPropertyAsFloat(Property.LEFT, 0f);
            float right = (float)this.GetPropertyAsFloat(Property.RIGHT, 0f);
            int reverseMultiplier = reverse ? -1 : 1;
            float dxRight = left != 0 ? left * reverseMultiplier : -right * reverseMultiplier;
            float dyUp = top != 0 ? -top * reverseMultiplier : bottom * reverseMultiplier;
            if (dxRight != 0 || dyUp != 0) {
                Move(dxRight, dyUp);
            }
        }

        protected internal virtual void ApplyDestination(PdfDocument document) {
            String destination = this.GetProperty<String>(Property.DESTINATION);
            if (destination != null) {
                PdfArray array = new PdfArray();
                array.Add(document.GetPage(occupiedArea.GetPageNumber()).GetPdfObject());
                array.Add(PdfName.XYZ);
                array.Add(new PdfNumber(occupiedArea.GetBBox().GetX()));
                array.Add(new PdfNumber(occupiedArea.GetBBox().GetY() + occupiedArea.GetBBox().GetHeight()));
                array.Add(new PdfNumber(0));
                document.AddNamedDestination(destination, ((PdfArray)array.MakeIndirect(document)));
                DeleteProperty(Property.DESTINATION);
            }
        }

        protected internal virtual void ApplyAction(PdfDocument document) {
            PdfAction action = this.GetProperty<PdfAction>(Property.ACTION);
            if (action != null) {
                PdfLinkAnnotation link = this.GetProperty<PdfLinkAnnotation>(Property.LINK_ANNOTATION);
                if (link == null) {
                    link = (PdfLinkAnnotation)new PdfLinkAnnotation(new Rectangle(0, 0, 0, 0)).SetFlags(PdfAnnotation.PRINT);
                    Border border = this.GetProperty<Border>(Property.BORDER);
                    if (border != null) {
                        link.SetBorder(new PdfArray(new float[] { 0, 0, border.GetWidth() }));
                    }
                    else {
                        link.SetBorder(new PdfArray(new float[] { 0, 0, 0 }));
                    }
                    SetProperty(Property.LINK_ANNOTATION, link);
                }
                link.SetAction(action);
            }
        }

        protected internal virtual void ApplyLinkAnnotation(PdfDocument document) {
            PdfLinkAnnotation linkAnnotation = this.GetProperty<PdfLinkAnnotation>(Property.LINK_ANNOTATION);
            if (linkAnnotation != null) {
                Rectangle pdfBBox = CalculateAbsolutePdfBBox();
                linkAnnotation.SetRectangle(new PdfArray(pdfBBox));
                PdfPage page = document.GetPage(occupiedArea.GetPageNumber());
                page.AddAnnotation(linkAnnotation);
            }
        }

        protected internal virtual void UpdateHeightsOnSplit(bool wasHeightClipped, iText.Layout.Renderer.AbstractRenderer
             splitRenderer, iText.Layout.Renderer.AbstractRenderer overflowRenderer) {
            float? maxHeight = RetrieveMaxHeight();
            if (maxHeight != null) {
                overflowRenderer.UpdateMaxHeight(maxHeight - occupiedArea.GetBBox().GetHeight());
            }
            float? minHeight = RetrieveMinHeight();
            if (minHeight != null) {
                overflowRenderer.UpdateMinHeight(minHeight - occupiedArea.GetBBox().GetHeight());
            }
            float? height = RetrieveHeight();
            if (height != null) {
                overflowRenderer.UpdateHeight(height - occupiedArea.GetBBox().GetHeight());
            }
            if (wasHeightClipped) {
                ILogger logger = LoggerFactory.GetLogger(typeof(BlockRenderer));
                logger.Warn(iText.IO.LogMessageConstant.CLIP_ELEMENT);
                splitRenderer.occupiedArea.GetBBox().MoveDown((float)maxHeight - occupiedArea.GetBBox().GetHeight()).SetHeight
                    ((float)maxHeight);
            }
        }

        protected internal virtual MinMaxWidth GetMinMaxWidth(float availableWidth) {
            return MinMaxWidthUtils.CountDefaultMinMaxWidth(this, availableWidth);
        }

        protected internal virtual bool SetMinMaxWidthBasedOnFixedWidth(MinMaxWidth minMaxWidth) {
            // retrieve returns max width, if there is no width.
            if (HasAbsoluteUnitValue(Property.WIDTH)) {
                //Renderer may override retrieveWidth, double check is required.
                float? width = RetrieveWidth(0);
                if (width != null) {
                    minMaxWidth.SetChildrenMaxWidth((float)width);
                    minMaxWidth.SetChildrenMinWidth((float)width);
                    return true;
                }
            }
            return false;
        }

        protected internal virtual bool IsNotFittingHeight(LayoutArea layoutArea) {
            return !IsPositioned() && occupiedArea.GetBBox().GetHeight() > layoutArea.GetBBox().GetHeight();
        }

        protected internal virtual bool IsNotFittingWidth(LayoutArea layoutArea) {
            return !IsPositioned() && occupiedArea.GetBBox().GetWidth() > layoutArea.GetBBox().GetWidth();
        }

        protected internal virtual bool IsNotFittingLayoutArea(LayoutArea layoutArea) {
            return IsNotFittingHeight(layoutArea) || IsNotFittingWidth(layoutArea);
        }

        /// <summary>Indicates whether the renderer's position is fixed or not.</summary>
        /// <returns>
        /// a
        /// <c>boolean</c>
        /// </returns>
        protected internal virtual bool IsPositioned() {
            return !IsStaticLayout();
        }

        /// <summary>Indicates whether the renderer's position is fixed or not.</summary>
        /// <returns>
        /// a
        /// <c>boolean</c>
        /// </returns>
        protected internal virtual bool IsFixedLayout() {
            Object positioning = this.GetProperty<Object>(Property.POSITION);
            return System.Convert.ToInt32(LayoutPosition.FIXED).Equals(positioning);
        }

        protected internal virtual bool IsStaticLayout() {
            Object positioning = this.GetProperty<Object>(Property.POSITION);
            return positioning == null || System.Convert.ToInt32(LayoutPosition.STATIC).Equals(positioning);
        }

        protected internal virtual bool IsRelativePosition() {
            int? positioning = this.GetPropertyAsInteger(Property.POSITION);
            return System.Convert.ToInt32(LayoutPosition.RELATIVE).Equals(positioning);
        }

        protected internal virtual bool IsAbsolutePosition() {
            int? positioning = this.GetPropertyAsInteger(Property.POSITION);
            return System.Convert.ToInt32(LayoutPosition.ABSOLUTE).Equals(positioning);
        }

        protected internal virtual bool IsKeepTogether() {
            return true.Equals(GetPropertyAsBoolean(Property.KEEP_TOGETHER));
        }

        [Obsolete]
        protected internal virtual void AlignChildHorizontally(IRenderer childRenderer, float availableWidth) {
            HorizontalAlignment? horizontalAlignment = childRenderer.GetProperty<HorizontalAlignment?>(Property.HORIZONTAL_ALIGNMENT
                );
            if (horizontalAlignment != null && horizontalAlignment != HorizontalAlignment.LEFT) {
                float freeSpace = availableWidth - childRenderer.GetOccupiedArea().GetBBox().GetWidth();
                switch (horizontalAlignment) {
                    case HorizontalAlignment.RIGHT: {
                        childRenderer.Move(freeSpace, 0);
                        break;
                    }

                    case HorizontalAlignment.CENTER: {
                        childRenderer.Move(freeSpace / 2, 0);
                        break;
                    }
                }
            }
        }

        // Note! The second parameter is here on purpose. Currently occupied area is passed as a value of this parameter in
        // BlockRenderer, but actually, the block can have many areas, and occupied area will be the common area of sub-areas,
        // whereas child element alignment should be performed area-wise.
        protected internal virtual void AlignChildHorizontally(IRenderer childRenderer, Rectangle currentArea) {
            float availableWidth = currentArea.GetWidth();
            HorizontalAlignment? horizontalAlignment = childRenderer.GetProperty<HorizontalAlignment?>(Property.HORIZONTAL_ALIGNMENT
                );
            if (horizontalAlignment != null && horizontalAlignment != HorizontalAlignment.LEFT) {
                float freeSpace = availableWidth - childRenderer.GetOccupiedArea().GetBBox().GetWidth();
                if (freeSpace > 0) {
                    try {
                        switch (horizontalAlignment) {
                            case HorizontalAlignment.RIGHT: {
                                childRenderer.Move(freeSpace, 0);
                                break;
                            }

                            case HorizontalAlignment.CENTER: {
                                childRenderer.Move(freeSpace / 2, 0);
                                break;
                            }
                        }
                    }
                    catch (Exception) {
                        // TODO Review exception type when DEVSIX-1592 is resolved.
                        ILogger logger = LoggerFactory.GetLogger(typeof(iText.Layout.Renderer.AbstractRenderer));
                        logger.Error(MessageFormatUtil.Format(iText.IO.LogMessageConstant.OCCUPIED_AREA_HAS_NOT_BEEN_INITIALIZED, 
                            "Some of the children might not end up aligned horizontally."));
                    }
                }
            }
        }

        /// <summary>Gets borders of the element in the specified order: top, right, bottom, left.</summary>
        /// <returns>
        /// an array of BorderDrawer objects.
        /// In case when certain border isn't set <code>Property.BORDER</code> is used,
        /// and if <code>Property.BORDER</code> is also not set then <code>null<code/> is returned
        /// on position of this border
        /// </returns>
        protected internal virtual Border[] GetBorders() {
            return GetBorders(this);
        }

        protected internal virtual iText.Layout.Renderer.AbstractRenderer SetBorders(Border border, int borderNumber
            ) {
            switch (borderNumber) {
                case 0: {
                    SetProperty(Property.BORDER_TOP, border);
                    break;
                }

                case 1: {
                    SetProperty(Property.BORDER_RIGHT, border);
                    break;
                }

                case 2: {
                    SetProperty(Property.BORDER_BOTTOM, border);
                    break;
                }

                case 3: {
                    SetProperty(Property.BORDER_LEFT, border);
                    break;
                }
            }
            return this;
        }

        /// <summary>
        /// Calculates the bounding box of the content in the coordinate system of the pdf entity on which content is placed,
        /// e.g.
        /// </summary>
        /// <remarks>
        /// Calculates the bounding box of the content in the coordinate system of the pdf entity on which content is placed,
        /// e.g. document page or form xObject. This is particularly useful for the cases when element is nested in the rotated
        /// element.
        /// </remarks>
        /// <returns>
        /// a
        /// <see cref="iText.Kernel.Geom.Rectangle"/>
        /// which is a bbox of the content not relative to the parent's layout area but rather to
        /// the some pdf entity coordinate system.
        /// </returns>
        protected internal virtual Rectangle CalculateAbsolutePdfBBox() {
            Rectangle contentBox = GetOccupiedAreaBBox();
            IList<Point> contentBoxPoints = RectangleToPointsList(contentBox);
            iText.Layout.Renderer.AbstractRenderer renderer = this;
            while (renderer.parent != null) {
                if (renderer is BlockRenderer) {
                    float? angle = renderer.GetProperty<float?>(Property.ROTATION_ANGLE);
                    if (angle != null) {
                        BlockRenderer blockRenderer = (BlockRenderer)renderer;
                        AffineTransform rotationTransform = blockRenderer.CreateRotationTransformInsideOccupiedArea();
                        TransformPoints(contentBoxPoints, rotationTransform);
                    }
                }
                if (renderer.GetProperty<Transform>(Property.TRANSFORM) != null) {
                    if (renderer is BlockRenderer || renderer is ImageRenderer || renderer is TableRenderer) {
                        AffineTransform rotationTransform = renderer.CreateTransformationInsideOccupiedArea();
                        TransformPoints(contentBoxPoints, rotationTransform);
                    }
                }
                renderer = (iText.Layout.Renderer.AbstractRenderer)renderer.parent;
            }
            return CalculateBBox(contentBoxPoints);
        }

        /// <summary>Calculates bounding box around points.</summary>
        /// <param name="points">list of the points calculated bbox will enclose.</param>
        /// <returns>array of float values which denote left, bottom, right, top lines of bbox in this specific order</returns>
        protected internal virtual Rectangle CalculateBBox(IList<Point> points) {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = -double.MaxValue;
            double maxY = -double.MaxValue;
            foreach (Point p in points) {
                minX = Math.Min(p.GetX(), minX);
                minY = Math.Min(p.GetY(), minY);
                maxX = Math.Max(p.GetX(), maxX);
                maxY = Math.Max(p.GetY(), maxY);
            }
            return new Rectangle((float)minX, (float)minY, (float)(maxX - minX), (float)(maxY - minY));
        }

        protected internal virtual IList<Point> RectangleToPointsList(Rectangle rect) {
            IList<Point> points = new List<Point>();
            points.AddAll(iText.IO.Util.JavaUtil.ArraysAsList(new Point(rect.GetLeft(), rect.GetBottom()), new Point(rect
                .GetRight(), rect.GetBottom()), new Point(rect.GetRight(), rect.GetTop()), new Point(rect.GetLeft(), rect
                .GetTop())));
            return points;
        }

        protected internal virtual IList<Point> TransformPoints(IList<Point> points, AffineTransform transform) {
            foreach (Point point in points) {
                transform.Transform(point, point);
            }
            return points;
        }

        /// <summary>
        /// This method calculates the shift needed to be applied to the points in order to position
        /// upper and left borders of their bounding box at the given lines.
        /// </summary>
        /// <param name="left">x coordinate at which points bbox left border is to be aligned</param>
        /// <param name="top">y coordinate at which points bbox upper border is to be aligned</param>
        /// <param name="points">the points, which bbox will be aligned at the given position</param>
        /// <returns>
        /// array of two floats, where first element denotes x-coordinate shift and the second
        /// element denotes y-coordinate shift which are needed to align points bbox at the given lines.
        /// </returns>
        protected internal virtual float[] CalculateShiftToPositionBBoxOfPointsAt(float left, float top, IList<Point
            > points) {
            double minX = double.MaxValue;
            double maxY = -double.MaxValue;
            foreach (Point point in points) {
                minX = Math.Min(point.GetX(), minX);
                maxY = Math.Max(point.GetY(), maxY);
            }
            float dx = (float)(left - minX);
            float dy = (float)(top - maxY);
            return new float[] { dx, dy };
        }

        protected internal virtual void OverrideHeightProperties() {
            float? height = GetPropertyAsFloat(Property.HEIGHT);
            float? maxHeight = GetPropertyAsFloat(Property.MAX_HEIGHT);
            float? minHeight = GetPropertyAsFloat(Property.MIN_HEIGHT);
            if (null != height) {
                if (null == maxHeight || height < maxHeight) {
                    maxHeight = height;
                }
                else {
                    height = maxHeight;
                }
                if (null == minHeight || height > minHeight) {
                    minHeight = height;
                }
            }
            if (null != maxHeight && null != minHeight && minHeight > maxHeight) {
                maxHeight = minHeight;
            }
            if (null != maxHeight) {
                SetProperty(Property.MAX_HEIGHT, maxHeight);
            }
            if (null != minHeight) {
                SetProperty(Property.MIN_HEIGHT, minHeight);
            }
        }

        /// <summary>Check if corresponding property has point value.</summary>
        /// <param name="property">
        /// 
        /// <see cref="iText.Layout.Properties.Property"/>
        /// </param>
        /// <returns>false if property value either null, or percent, otherwise true.</returns>
        protected internal virtual bool HasAbsoluteUnitValue(int property) {
            UnitValue value = this.GetProperty<UnitValue>(property);
            return value != null && value.IsPointValue();
        }

        internal virtual bool IsFirstOnRootArea() {
            bool isFirstOnRootArea = true;
            iText.Layout.Renderer.AbstractRenderer ancestor = this;
            while (isFirstOnRootArea && ancestor.GetParent() != null) {
                IRenderer parent = ancestor.GetParent();
                if (parent is RootRenderer) {
                    isFirstOnRootArea = ((RootRenderer)parent).GetCurrentArea().IsEmptyArea();
                }
                else {
                    isFirstOnRootArea = parent.GetOccupiedArea().GetBBox().GetHeight() < EPS;
                }
                if (!(parent is iText.Layout.Renderer.AbstractRenderer)) {
                    break;
                }
                ancestor = (iText.Layout.Renderer.AbstractRenderer)parent;
            }
            return isFirstOnRootArea;
        }

        internal virtual RootRenderer GetRootRenderer() {
            IRenderer currentRenderer = this;
            while (currentRenderer is iText.Layout.Renderer.AbstractRenderer) {
                if (currentRenderer is RootRenderer) {
                    return (RootRenderer)currentRenderer;
                }
                currentRenderer = ((iText.Layout.Renderer.AbstractRenderer)currentRenderer).GetParent();
            }
            return null;
        }

        internal static float CalculateAdditionalWidth(iText.Layout.Renderer.AbstractRenderer renderer) {
            Rectangle dummy = new Rectangle(0, 0);
            renderer.ApplyMargins(dummy, true);
            renderer.ApplyBorderBox(dummy, true);
            renderer.ApplyPaddings(dummy, true);
            return dummy.GetWidth();
        }

        internal static bool NoAbsolutePositionInfo(IRenderer renderer) {
            return !renderer.HasProperty(Property.TOP) && !renderer.HasProperty(Property.BOTTOM) && !renderer.HasProperty
                (Property.LEFT) && !renderer.HasProperty(Property.RIGHT);
        }

        internal static float? GetPropertyAsFloat(IRenderer renderer, int property) {
            return NumberUtil.AsFloat(renderer.GetProperty<Object>(property));
        }

        internal static void ApplyGeneratedAccessibleAttributes(TagTreePointer tagPointer, PdfDictionary attributes
            ) {
            if (attributes == null) {
                return;
            }
            // TODO if taggingPointer.getProperties will always write directly to struct elem, use it instead (add addAttributes overload with index)
            PdfStructElem structElem = tagPointer.GetDocument().GetTagStructureContext().GetPointerStructElem(tagPointer
                );
            PdfObject structElemAttr = structElem.GetAttributes(false);
            if (structElemAttr == null || !structElemAttr.IsDictionary() && !structElemAttr.IsArray()) {
                structElem.SetAttributes(attributes);
            }
            else {
                if (structElemAttr.IsDictionary()) {
                    PdfArray attrArr = new PdfArray();
                    attrArr.Add(attributes);
                    attrArr.Add(structElemAttr);
                    structElem.SetAttributes(attrArr);
                }
                else {
                    // isArray
                    PdfArray attrArr = (PdfArray)structElemAttr;
                    attrArr.Add(0, attributes);
                }
            }
        }

        internal virtual void ShrinkOccupiedAreaForAbsolutePosition() {
            // In case of absolute positioning and not specified left, right, width values, the parent box is shrunk to fit
            // the children. It does not occupy all the available width if it does not need to.
            if (IsAbsolutePosition()) {
                float? left = this.GetPropertyAsFloat(Property.LEFT);
                float? right = this.GetPropertyAsFloat(Property.RIGHT);
                UnitValue width = this.GetProperty<UnitValue>(Property.WIDTH);
                if (left == null && right == null && width == null) {
                    occupiedArea.GetBBox().SetWidth(0);
                }
            }
        }

        internal virtual void DrawPositionedChildren(DrawContext drawContext) {
            foreach (IRenderer positionedChild in positionedRenderers) {
                positionedChild.Draw(drawContext);
            }
        }

        internal virtual FontCharacteristics CreateFontCharacteristics() {
            FontCharacteristics fc = new FontCharacteristics();
            if (this.HasProperty(Property.FONT_WEIGHT)) {
                fc.SetFontWeight((String)this.GetProperty<Object>(Property.FONT_WEIGHT));
            }
            if (this.HasProperty(Property.FONT_STYLE)) {
                fc.SetFontStyle((String)this.GetProperty<Object>(Property.FONT_STYLE));
            }
            return fc;
        }

        // This method is intended to get first valid PdfFont in this renderer, based of font property.
        // It is usually done for counting some layout characteristics like ascender or descender.
        // NOTE: It neither change Font Property of renderer, nor is guarantied to contain all glyphs used in renderer.
        internal virtual PdfFont ResolveFirstPdfFont() {
            Object font = this.GetProperty<Object>(Property.FONT);
            if (font is PdfFont) {
                return (PdfFont)font;
            }
            else {
                if (font is String) {
                    FontProvider provider = this.GetProperty<FontProvider>(Property.FONT_PROVIDER);
                    if (provider == null) {
                        throw new InvalidOperationException("Invalid font type. FontProvider expected. Cannot resolve font with string value"
                            );
                    }
                    FontCharacteristics fc = CreateFontCharacteristics();
                    return ResolveFirstPdfFont((String)font, provider, fc);
                }
                else {
                    throw new InvalidOperationException("String or PdfFont expected as value of FONT property");
                }
            }
        }

        // This method is intended to get first valid PdfFont described in font string,
        // with specific FontCharacteristics with the help of specified font provider.
        // This method is intended to be called from previous method that deals with Font Property.
        // NOTE: It neither change Font Property of renderer, nor is guarantied to contain all glyphs used in renderer.
        // TODO this mechanism does not take text into account
        internal virtual PdfFont ResolveFirstPdfFont(String font, FontProvider provider, FontCharacteristics fc) {
            return provider.GetPdfFont(provider.GetFontSelector(FontFamilySplitter.SplitFontFamily(font), fc).BestMatch
                ());
        }

        internal virtual void ApplyAbsolutePositionIfNeeded(LayoutContext layoutContext) {
            if (IsAbsolutePosition()) {
                ApplyAbsolutePosition(layoutContext is PositionedLayoutContext ? ((PositionedLayoutContext)layoutContext).
                    GetParentOccupiedArea().GetBBox() : layoutContext.GetArea().GetBBox());
            }
        }

        internal virtual void PreparePositionedRendererAndAreaForLayout(IRenderer childPositionedRenderer, Rectangle
             fullBbox, Rectangle parentBbox) {
            float? left = GetPropertyAsFloat(childPositionedRenderer, Property.LEFT);
            float? right = GetPropertyAsFloat(childPositionedRenderer, Property.RIGHT);
            float? top = GetPropertyAsFloat(childPositionedRenderer, Property.TOP);
            float? bottom = GetPropertyAsFloat(childPositionedRenderer, Property.BOTTOM);
            childPositionedRenderer.SetParent(this);
            AdjustPositionedRendererLayoutBoxWidth(childPositionedRenderer, fullBbox, left, right);
            if (System.Convert.ToInt32(LayoutPosition.ABSOLUTE).Equals(childPositionedRenderer.GetProperty<int?>(Property
                .POSITION))) {
                UpdateMinHeightForAbsolutelyPositionedRenderer(childPositionedRenderer, parentBbox, top, bottom);
            }
        }

        private void UpdateMinHeightForAbsolutelyPositionedRenderer(IRenderer renderer, Rectangle parentRendererBox
            , float? top, float? bottom) {
            if (top != null && bottom != null && !renderer.HasProperty(Property.HEIGHT)) {
                float? currentMaxHeight = GetPropertyAsFloat(renderer, Property.MAX_HEIGHT);
                float? currentMinHeight = GetPropertyAsFloat(renderer, Property.MIN_HEIGHT);
                float resolvedMinHeight = Math.Max(0, parentRendererBox.GetTop() - (float)top - parentRendererBox.GetBottom
                    () - (float)bottom);
                Rectangle dummy = new Rectangle(0, 0);
                if (!IsBorderBoxSizing(renderer)) {
                    ApplyPaddings(dummy, GetPaddings(renderer), true);
                    ApplyBorderBox(dummy, GetBorders(renderer), true);
                }
                ApplyMargins(dummy, GetMargins(renderer), true);
                resolvedMinHeight -= dummy.GetHeight();
                if (currentMinHeight != null) {
                    resolvedMinHeight = Math.Max(resolvedMinHeight, (float)currentMinHeight);
                }
                if (currentMaxHeight != null) {
                    resolvedMinHeight = Math.Min(resolvedMinHeight, (float)currentMaxHeight);
                }
                renderer.SetProperty(Property.MIN_HEIGHT, resolvedMinHeight);
            }
        }

        private void AdjustPositionedRendererLayoutBoxWidth(IRenderer renderer, Rectangle fullBbox, float? left, float?
             right) {
            if (left != null) {
                fullBbox.SetWidth(fullBbox.GetWidth() - (float)left).SetX(fullBbox.GetX() + (float)left);
            }
            if (right != null) {
                fullBbox.SetWidth(fullBbox.GetWidth() - (float)right);
            }
            if (left == null && right == null && !renderer.HasProperty(Property.WIDTH)) {
                // Other, non-block renderers won't occupy full width anyway
                MinMaxWidth minMaxWidth = renderer is BlockRenderer ? ((BlockRenderer)renderer).GetMinMaxWidth(MinMaxWidthUtils
                    .GetMax()) : null;
                if (minMaxWidth != null && minMaxWidth.GetMaxWidth() < fullBbox.GetWidth()) {
                    fullBbox.SetWidth(minMaxWidth.GetMaxWidth() + iText.Layout.Renderer.AbstractRenderer.EPS);
                }
            }
        }

        private static float CalculatePaddingBorderWidth(iText.Layout.Renderer.AbstractRenderer renderer) {
            Rectangle dummy = new Rectangle(0, 0);
            renderer.ApplyBorderBox(dummy, true);
            renderer.ApplyPaddings(dummy, true);
            return dummy.GetWidth();
        }

        private static float CalculatePaddingBorderHeight(iText.Layout.Renderer.AbstractRenderer renderer) {
            Rectangle dummy = new Rectangle(0, 0);
            renderer.ApplyBorderBox(dummy, true);
            renderer.ApplyPaddings(dummy, true);
            return dummy.GetHeight();
        }

        /// <summary>
        /// This method creates
        /// <see cref="iText.Kernel.Geom.AffineTransform"/>
        /// instance that could be used
        /// to transform content inside the occupied area,
        /// considering the centre of the occupiedArea as the origin of a coordinate system for transformation.
        /// </summary>
        /// <returns>
        /// 
        /// <see cref="iText.Kernel.Geom.AffineTransform"/>
        /// that transforms the content and places it inside occupied area.
        /// </returns>
        private AffineTransform CreateTransformationInsideOccupiedArea() {
            Rectangle backgroundArea = ApplyMargins(occupiedArea.Clone().GetBBox(), false);
            float x = backgroundArea.GetX();
            float y = backgroundArea.GetY();
            float height = backgroundArea.GetHeight();
            float width = backgroundArea.GetWidth();
            AffineTransform transform = AffineTransform.GetTranslateInstance(-1 * (x + width / 2), -1 * (y + height / 
                2));
            transform.PreConcatenate(Transform.GetAffineTransform(this.GetProperty<Transform>(Property.TRANSFORM), width
                , height));
            transform.PreConcatenate(AffineTransform.GetTranslateInstance(x + width / 2, y + height / 2));
            return transform;
        }

        protected internal virtual void BeginTranformationIfApplied(PdfCanvas canvas) {
            if (this.GetProperty<Transform>(Property.TRANSFORM) != null) {
                AffineTransform transform = CreateTransformationInsideOccupiedArea();
                canvas.SaveState().ConcatMatrix(transform);
            }
        }

        protected internal virtual void EndTranformationIfApplied(PdfCanvas canvas) {
            if (this.GetProperty<Transform>(Property.TRANSFORM) != null) {
                canvas.RestoreState();
            }
        }

        private static float[] GetMargins(IRenderer renderer) {
            return new float[] { (float)NumberUtil.AsFloat(renderer.GetProperty<Object>(Property.MARGIN_TOP)), (float)
                NumberUtil.AsFloat(renderer.GetProperty<Object>(Property.MARGIN_RIGHT)), (float)NumberUtil.AsFloat(renderer
                .GetProperty<Object>(Property.MARGIN_BOTTOM)), (float)NumberUtil.AsFloat(renderer.GetProperty<Object>(
                Property.MARGIN_LEFT)) };
        }

        private static Border[] GetBorders(IRenderer renderer) {
            Border border = renderer.GetProperty<Border>(Property.BORDER);
            Border topBorder = renderer.GetProperty<Border>(Property.BORDER_TOP);
            Border rightBorder = renderer.GetProperty<Border>(Property.BORDER_RIGHT);
            Border bottomBorder = renderer.GetProperty<Border>(Property.BORDER_BOTTOM);
            Border leftBorder = renderer.GetProperty<Border>(Property.BORDER_LEFT);
            Border[] borders = new Border[] { topBorder, rightBorder, bottomBorder, leftBorder };
            if (!HasOwnOrModelProperty(renderer, Property.BORDER_TOP)) {
                borders[0] = border;
            }
            if (!HasOwnOrModelProperty(renderer, Property.BORDER_RIGHT)) {
                borders[1] = border;
            }
            if (!HasOwnOrModelProperty(renderer, Property.BORDER_BOTTOM)) {
                borders[2] = border;
            }
            if (!HasOwnOrModelProperty(renderer, Property.BORDER_LEFT)) {
                borders[3] = border;
            }
            return borders;
        }

        private static float[] GetPaddings(IRenderer renderer) {
            return new float[] { (float)NumberUtil.AsFloat(renderer.GetProperty<Object>(Property.PADDING_TOP)), (float
                )NumberUtil.AsFloat(renderer.GetProperty<Object>(Property.PADDING_RIGHT)), (float)NumberUtil.AsFloat(renderer
                .GetProperty<Object>(Property.PADDING_BOTTOM)), (float)NumberUtil.AsFloat(renderer.GetProperty<Object>
                (Property.PADDING_LEFT)) };
        }

        private static bool HasOwnOrModelProperty(IRenderer renderer, int property) {
            return renderer.HasOwnProperty(property) || (null != renderer.GetModelElement() && renderer.GetModelElement
                ().HasProperty(property));
        }

        public abstract IRenderer GetNextRenderer();

        public abstract LayoutResult Layout(LayoutContext arg1);
    }
}
