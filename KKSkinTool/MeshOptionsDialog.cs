using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KKSkinTool
{
    /// <summary>OBJ 그룹(서브메쉬·재질) 선택 + 앞 방향</summary>
    public static class MeshOptionsDialog
    {
        public static bool Ask(IWin32Window owner, ObjFile obj, string[] prevGroups, string prevAxis, out string[] groups, out string axis)
        {
            groups = null; axis = prevAxis ?? "+z";
            using (var f = new Form { Text = "메쉬 설정 — " + System.IO.Path.GetFileName(obj.Path), Width = 520, Height = 460, StartPosition = FormStartPosition.CenterParent,
                                      FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, Font = new Font("Malgun Gothic", 9f) })
            {
                var form = owner as Form; if (form != null) f.Icon = form.Icon;
                var lbl = new Label { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 8, 8, 0),
                    Text = string.Format("정점 {0:N0} · UV {1:N0} · 면 {2:N0}\n투영·보기에 쓸 그룹(서브메쉬/재질)을 고르세요. 텍스처 하나에 해당하는 것만 고르는 게 좋습니다.", obj.V.Length / 3, obj.VT.Length / 2, obj.FaceGroup.Length) };
                var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
                var names = obj.Groups.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
                foreach (var n in names)
                    list.Items.Add(string.Format("{0}   ({1:N0} 면)", n, obj.Groups[n]), prevGroups == null || prevGroups.Contains(n));
                var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 6, 8, 6), FlowDirection = FlowDirection.LeftToRight };
                bottom.Controls.Add(new Label { Text = "앞 방향", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
                var cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
                cmb.Items.AddRange(new object[] { "+z", "-z", "+x", "-x" }); cmb.SelectedItem = axis; if (cmb.SelectedIndex < 0) cmb.SelectedIndex = 0;
                bottom.Controls.Add(cmb);
                var all = new Button { Text = "전체", Width = 60 }; all.Click += (s, e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };
                var none = new Button { Text = "해제", Width = 60 }; none.Click += (s, e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };
                var ok = new Button { Text = "확인", Width = 80, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "취소", Width = 80, DialogResult = DialogResult.Cancel };
                bottom.Controls.AddRange(new Control[] { all, none, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                f.Controls.Add(list); f.Controls.Add(lbl); f.Controls.Add(bottom);
                if (f.ShowDialog(owner) != DialogResult.OK) return false;
                var sel = list.CheckedIndices.Cast<int>().Select(i => names[i]).ToArray();
                if (sel.Length == 0) { MessageBox.Show(owner, "그룹을 하나 이상 선택하세요."); return false; }
                groups = sel; axis = (string)cmb.SelectedItem; return true;
            }
        }
    }
}
