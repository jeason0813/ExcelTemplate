﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ExportTemplate.Export.Entity;
using ExportTemplate.Export.Util;
using NPOI.SS.UserModel;
using NPOI.SS.Util;

namespace ExportTemplate.Export.Writer
{
    public class CellWriter : BaseWriter
    {
        public Source Source
        {
            get
            {
                Cell cell = Entity as Cell;
                return cell.ProductRule.GetSource(cell.SourceName);
            }
        }

        public CellWriter(Cell cell, BaseWriterContainer parent) : base(cell, parent) { }

        /// <summary>
        /// 是否可填充：否
        /// </summary>
        public override bool CopyFill { get { return false; } }

        protected override void Writing(object sender, WriteEventArgs args)
        {
            Cell cell = args.Entity as Cell;
            ISheet exSheet = args.ExSheet;
            if (cell == null) return;

            object tmpObject = this.GetValue();
            if (tmpObject == null) return;

            IWorkbook book = exSheet.Workbook;
            //IDrawing draw = exSheet.DrawingPatriarch ?? exSheet.CreateDrawingPatriarch();
            IDrawing draw = exSheet.DrawingPatriarch ?? exSheet.CreateDrawingPatriarch();//只能有一个实例，否则只有最后一个对象生效

            int rowIndex = this.RowIndex;
            int colIndex = this.ColIndex;
            IRow exRow = exSheet.GetRow(rowIndex) ?? exSheet.CreateRow(rowIndex);
            //if (exRow != null)
            //{
            ICell exCell = exRow.GetCell(this.ColIndex) ?? exRow.CreateCell(this.ColIndex);
            //if (exCell != null)
            //{
            //object tmpObject = sheet.IsDynamic ? sheet.GetValue(cell,sheetName) : dt.Rows[cell.DataIndex][cell.Field];
            if (cell.ValueAppend)
            {
                tmpObject = exCell.StringCellValue + tmpObject;
            }
            if (tmpObject.GetType() == typeof(byte[]))//处理图片
            {
                CellRangeAddress region = NPOIExcelUtil.GetRange(exCell);

                Image image = new Bitmap(new MemoryStream(tmpObject as byte[]));
                Size size = image.Size; 
                if (size.IsEmpty) return;

                IClientAnchor anchor = region != null ?
                    draw.CreateAnchor(0, 0, 0, 0, region.FirstColumn, region.FirstRow, region.LastColumn + 1, region.LastRow + 1) :
                    draw.CreateAnchor(20, 20, 0, 0, colIndex, rowIndex, colIndex + this.ColCount, rowIndex + this.RowCount);
                
                IPicture pic = draw.CreatePicture(anchor, book.AddPicture((byte[])tmpObject, PictureType.JPEG));

                switch (cell.FillType)
                {
                    case FillType.Origin:
                        pic.Resize();
                        break;
                    case FillType.Scale:
                        float width = 0, height = 0;
                        for (int i = anchor.Col1; i < anchor.Col2; i++)
                        {
                            width += exSheet.GetColumnWidth(i) / 256f * 12;
                        }
                        for (int i = anchor.Row1; i < anchor.Row2; i++)
                        {
                            IRow row = exSheet.GetRow(i);
                            height += row != null ? row.HeightInPoints : exSheet.DefaultRowHeightInPoints;
                        }
                        float factor = Math.Min(width / (size.Width * 0.75f), height / (size.Height * 0.75f));
                        pic.Resize(factor);
                        break;
                    default:
                        break;
                }
            }
            else
            {
                exCell.SetCellValue((tmpObject ?? "").ToString());
            }
            //}
            //}
        }

        public override int ColCount
        {
            get { return Entity.Location.ColCount > 0 ? Entity.Location.RowCount : 1; }
        }

        public override int RowCount
        {
            get { return Entity.Location.RowCount > 0 ? Entity.Location.RowCount : 1; }
        }

        public override IList<OutputNode> GetNodes()
        {
            return new List<OutputNode>(new OutputNode[]{
                new OutputNode(){
                    ColumnIndex = this.ColIndex,
                    ColumnSpan = this.ColCount,
                    RowIndex = this.RowIndex,
                    RowSpan = this.RowCount,
                    Content = this.GetValue(),
                    ValueAppend = (this.Entity as Cell).ValueAppend
                }
            });
        }

        /// <summary>
        /// 根据自身数据源解析后的值
        /// </summary>
        /// <returns></returns>
        public object GetValue()
        {
            Cell cell = Entity as Cell;
            //优先使用Field（Field取出数据保留原有数据类型，而Value用于支持以表达式形式的字符串输出）
            if (!string.IsNullOrEmpty(cell.Field))
            {
                return this.Source != null && this.Source.Table != null ? this.Source.Table.Rows[cell.DataIndex][cell.Field] : null;
            }
            Dictionary<string, object> dicts = new Dictionary<string, object>();
            string expression = cell.Value;
            Source tmpSource = this.Source;//GetSource();
            //处理当前Source表达式
            if (tmpSource != null && tmpSource.Table != null)
            {
                DataTable tmpTable = tmpSource.Table;
                DataRow row = tmpTable.Rows[cell.DataIndex];
                dicts.Add("RowNum", cell.DataIndex + 1);
                dicts.Add("Index", cell.DataIndex);
                foreach (DataColumn column in tmpTable.Columns)
                {
                    if (!dicts.ContainsKey(column.ColumnName))
                        dicts.Add(column.ColumnName, row[column.ColumnName]);
                    else
                        dicts[column.ColumnName] = row[column.ColumnName];//如果重复则替换
                }
            }
            MatchCollection matches = new Regex(@"(?:{)([\w\.]+)(?:})").Matches(expression);
            foreach (Match match in matches)
            {
                string fieldname = match.Groups[1].Value;
                if (dicts.Count > 0 && dicts.ContainsKey(fieldname))
                {
                    expression = expression.Replace(string.Format("{{{0}}}", fieldname), (dicts[fieldname] ?? "").ToString());
                }
            }
            return expression;
        }

    }
}
