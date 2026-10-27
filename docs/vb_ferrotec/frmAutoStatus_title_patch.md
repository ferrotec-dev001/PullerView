# VB_FERROTEC 側の最小変更（PullerView 連動用）

PullerView は外部プロセスなので `frmAutoStatus.MdiParent` を直接読めません。代わりに frmMain の MDIClient に
`WM_MDIGETACTIVE` を送って「現在アクティブな MDI 子フォーム」を取得し、それが frmAutoStatus かどうかを
**ウィンドウタイトル（Form.Text）** で判定します。frmAutoStatus の Text は現在空なので、次の 1 行を追加してください。

## frmAutoStatus.vb（Load イベント）
```vb
Private Sub frmAutoStatus_Load(sender As Object, e As EventArgs) Handles MyBase.Load
    Me.Text = "frmAutoStatus"   ' PullerView 連動用（FormBorderStyle=None のため画面には表示されない）
    ' ... 既存処理 ...
End Sub
```
または frmMain.vb の表示箇所：
```vb
Case Enum_Mode.eAuto
    Call Me.sub_CloseAllMdiChildrenForm()
    frmAutoStatus = Nothing
    With frmAutoStatus
        .MdiParent = Me
        .Text = "frmAutoStatus"     ' 追加
        .Show()
    End With
```
（MANUAL TUNE GRAPH / FINE TUNE GRAPH で frmAutoStatus を表示する箇所も同様）

## PullerView 側の動作
| VB_FERROTEC の状態 | PullerView |
|---|---|
| アクティブ MDI 子 = frmAutoStatus（MdiParent = Me で Show） | 表示（アイコン化前の最大化／最小化状態に復帰） |
| アクティブ MDI 子が他の画面、または MDI 子なし（CloseAllMdiChildren 後） | アイコン化 |
| VB_FERROTEC.exe 未実行 | 単体起動設定に従う |

`PullerView.ini [Host] autostatus_title=frmAutoStatus` が既定。タイトルを付けない場合は子孫コントロールの文字列
`autostatus_marker`（既定 AUTO STATUS）で判定します。判定結果は PullerView.log に
`MDI アクティブ子フォーム: title='…' client=1126x670 → frmAutoStatus` の形式で記録されます。
