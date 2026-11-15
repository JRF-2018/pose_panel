VrcPoseAliasesPanel {VERSION}
==============================

VRChat のアバターに、名前を付けて登録したポーズ(エイリアス)を選んでとらせる、小さな操作パネルです。
OscPoser の操作の口(MCP と同じ口)を使う例として作ったもので、ポーズを VRChat に送るのは OscPoser です。
OscPoser を使うのに、このパネルは要りません(OscPoser は単体で使えます)。よく使うポーズを名前で並べておくためのものです。
使い始めるには少し手間がかかります。詳しい説明は GitHub の README を読んでください:

  https://github.com/JRF-2018/pose_panel

■ 必要なもの
- Windows 10 / 11
- OscPoser 0.4.0 以降(https://github.com/JRF-2018/osc_poser)。ポーズをとらせるアバターに prefab を入れておく
- VRChat

■ はじめに
1. OscPoser の zip も展開し、OscPoser_<版> のフォルダをこのフォルダの中か横に置く(自動で見つけます)。
2. VrcPoseAliasesPanel.exe を起動する(OscPoser も開きます)。
3. ▣ でビューアー(VrmPoseViewer)を開き、最初に VRM をドロップする。
4. 例のポーズを使うなら、ねここやさんの無料の「ポーズ詰め合わせ」「Emote詰め合わせ」を Booth で入手し、
   ビューアーの「アニメーションの取り込み」に zip のままドロップする。
     https://necocoya.booth.pm/items/1634088
     https://necocoya.booth.pm/items/1065089
5. パネルの 0・1・2 で相手(VRChat のプロフィール番号。ふつうは 0)を選び、ポーズのメニューから選んで ➤ で送る。

■ エイリアス(pose_aliases.txt)
パネルのポーズの一覧は、このフォルダの pose_aliases.txt そのものです。
  1 行に  エイリアス名  anim  対象の.anim  [時刻]   (または  エイリアス名  pose  保存したポーズの名前)
  [グループ名] の行から次の [..] までがサブメニュー。並びはファイルに書いた順。
お気に入りのポーズで、自分の pose_aliases.txt を作っていくのがおすすめです。
並べ替えなどは、このファイルを直接編集してください(ビューアーの「pose_aliases.txt をメモ帳で編集」)。
1 つずつの登録・名前の変更・グループ分けは、ビューアーの「登録…」「編集…」でもできます。

■ プロフィール 0・1・2
VRChat は --profile=N で、同じ PC でいくつも同時に動かせます(それぞれ別のアカウントで)。
パネルの 0・1・2 はその番号で、OSC のポートは 9000・9010・9020。1 体だけなら 0 だけで足ります。
⏻ で、選んでいる番号の VRChat を起動します。

■ ライセンス
MIT License(LICENSE.txt)。ビューアーには UniVRM などが入っています(THIRD_PARTY_NOTICES.txt)。
付属の pose_aliases.txt の例(英語版 pose_aliases.en.txt も)は、ねここやさんのアセットの名前と場所だけで、アニメーションは含みません。

JRF ( http://jrf.cocolog-nifty.com/statuses , X: @jion_rockford )
Claude Opus 5.5 がほとんどを書きました。
