import json,sys
P="anim/nccy_pose_free/"; E="anim/nccy_emote_free/"
poses={
"立ち":[("010_0010","手を広げて立つ"),("010_0020","胸に手を当てる"),("010_0030","腕を抱える"),("010_0040","前かがみでピース"),
 ("010_0050","頭と腰に手"),("010_0060","片足を上げて手を振る"),("010_0070","すまし立ち"),("010_0080","拳を握って片足を上げる"),
 ("010_0090","腰に手"),("010_0100","両手を頬に"),("010_0110","腕を横に伸ばす"),("010_0120","片手を上げて片足立ち"),
 ("010_0130","跳ねるように手を広げる"),("010_0140","足を高く上げる"),("010_0150","手を前で組む"),("010_0160","振り返って前かがみ"),
 ("010_0170","手を広げて踏ん張る"),("010_0180","手を横に開いて片足立ち"),("010_0190","頭に手を当てる"),("010_0200","手を伸ばして振り返る")],
"座り":[("011_0010","足を投げ出して座る"),("011_0020","ぺたん座り"),("011_0030","横座り"),("011_0040","耳かき(する側)"),
 ("011_0050","しゃがんで手を合わせる"),("011_0060","体育座り"),("011_0070","あぐら"),("011_0080","片膝を立てて座る"),
 ("011_0090","しゃがんで片手をつく"),("011_0100","膝立ちで頭に手")],
"寝る・その他":[("012_0010","仰向け"),("012_0020","仰向けで口元に手"),("012_0030","大の字"),("012_0040","横向きに寝る"),
 ("012_0050","うつぶせ"),("012_0060","うつぶせで足を上げる"),("012_0070","寝そべって片肘をつく"),("012_0080","後ろに手をついて座る"),
 ("012_0090","丸まって寝る"),("012_0100","腕枕で寝る")],
"男性":[("020_0010","腕組み(男性)"),("020_0020","頭をかく"),("020_0030","身構える"),("020_0040","指さし(男性)"),
 ("020_0050","顎に手を当てて構える"),("020_0060","両手を腰に(男性)"),("020_0070","あぐら(男性)"),("020_0080","ヤンキー座り"),
 ("020_0090","寝そべって頬杖"),("020_0100","拳を突き上げる")],
"日常":[("030_0010","正座"),("030_0020","土下座"),("030_0030","颯爽と歩く"),("030_0040","膝をついてうなだれる"),
 ("030_0050","転ぶ"),("030_0060","スマホを見る"),("030_0061","電話する"),("030_0070","壁に手をつく"),
 ("030_0080","座って髪を結ぶ"),("030_0090","伸びをする"),("030_0100","手を差し出す")],
"ネタ":[("040_0010","ヤムチャしやがって…"),("040_0020","ゲッダン"),("040_0030","チェンジワイパー"),("040_0040","トンファーキック"),
 ("040_0050","犬神家"),("040_0060","八頭身1"),("040_0070","八頭身2"),("040_0080","八頭身3"),("040_0090","全力で走る"),
 ("040_0100","やらないか？"),("040_0110","クックロビン"),("040_0120","サタデーナイトフィーバー"),("040_0130","Daisuke"),
 ("040_0140","ホモォ…"),("040_0150","笹食ってる場合じゃねぇ"),("040_0160","Aポーズ"),("040_0170","歌舞伎"),
 ("040_0180","そげぶ1"),("040_0181","そげぶ2"),("040_0182","そげぶ3"),("040_0183","そげぶ4"),
 ("040_0190","ボディビル(正面)"),("040_0191","ボディビル(サイドチェスト)"),("040_0192","ボディビル(横)"),
 ("040_0200","特戦隊:クリーム"),("040_0201","特戦隊:バター"),("040_0202","特戦隊:チーズ"),("040_0203","特戦隊:ヨーグルト"),("040_0204","特戦隊:牛乳"),
 ("040_0210","ウマウマ"),("040_0220","ヨシッ！"),("040_0270","止まるんじゃねえぞ"),("040_0350","ゲッツ！"),("040_0360","岸田メル"),
 ("040_0370","外人4コマ1"),("040_0371","外人4コマ2"),("040_0372","外人4コマ3"),("040_0373","外人4コマ4"),("040_0374","外人4コマ5"),
 ("040_0380","へんなオジサン"),("040_0390","ななめニスト1"),("040_0391","ななめニスト2"),("040_0400","がに股で手を広げる"),("040_0401","手を上げて降参"),
 ("040_0410","天国行こう"),("040_0420","JOChallenge"),("040_0430","Just Do It!!(今やれ)"),
 ("040_0440","ドナルドダンス1"),("040_0441","ドナルドダンス2"),("040_0442","ドナルドダンス3"),("040_0443","ドナルドダンス4"),("040_0444","ドナルドダンス5"),
 ("040_0450","許されざる角度"),("040_0460","何がクリスマスじゃあい！"),("040_0470","粉☆バナナ"),("040_0480","GB宣言"),("040_0490","宇宙人×宇宙人"),
 ("040_0500","先生に言ってやろ☆1"),("040_0501","先生に言ってやろ☆2"),
 ("040_0510","少女漫画式カンフー1"),("040_0511","少女漫画式カンフー2"),("040_0512","少女漫画式カンフー3"),("040_0513","少女漫画式カンフー4"),("040_0514","少女漫画式カンフー5"),
 ("040_0520","お許しください！"),("040_0521","エ゛エーイ"),("040_0522","ウェイ"),("040_0530","ドローン"),
 ("040_0531","新衣装お披露目配信の時の追加機能"),("040_0532","配信時に急にお花摘みに行くVTuber")],
"ジャンプ":[("050_0010","両手を上げてジャンプ"),("050_0020","手を振ってジャンプ"),("050_0030","足を後ろに曲げてジャンプ"),("050_0040","片手を上げてジャンプ"),
 ("050_0050","手を胸の前にジャンプ"),("050_0060","片手を頭にジャンプ"),("050_0070","手を広げてジャンプ"),("050_0080","足を上げてジャンプ"),
 ("050_0090","両手を上げてジャンプ(男性)"),("050_0100","拳でジャンプ(男性)"),("050_0110","走りながらジャンプ"),("050_0120","大きく跳ぶ(男性)"),
 ("050_0130","のけぞってジャンプ"),("050_0140","片足で跳ぶ"),("050_0150","正座のままジャンプ"),("050_0160","ダイブ"),
 ("050_0170","光魔法かっこいいポーズ"),("050_0180","ガラスパーン"),("050_0190","後ろ向きにジャンプ"),("050_0200","手を広げて浮かぶ")],
}
hands=[("000_IdleFinger","指(ふだん)"),("010_Fist01","グー1"),("011_Fist02","グー2"),("012_Fist03","グー3"),
 ("020_HandOpen01","パー1"),("021_HandOpen02","パー2"),("022_HandOpen03","パー3"),("023_HandOpen04","パー4"),
 ("030_FingerPoint01","指さし"),("040_Victory01","ピース1"),("041_Victory02","ピース2"),("050_RocknRoll01","ロックンロール1"),("051_RocknRoll02","ロックンロール2"),
 ("060_HandGun01","指鉄砲"),("070_ThumbsUp01","親指を立てる"),("080_Kusukusu","くすくす"),("090_Oku","手を置く"),("091_Tsukamu","つかむ"),
 ("092_Tsumamu","つまむ"),("093_Nigiru","握る"),("100_Heart01","ハート(両手で)"),("110_Fox","キツネ"),("120_Money","OK・お金"),
 ("130_Yubikiri","指切り"),("140_Jibeta","地面に手をつく"),("150_Patchn","指パッチン"),("160_Wanawana","ワナワナ")]
emotes={
"エモート1":[("00_idle","待機(エモート)",None),("01_tefuri1","手振り1",2.57),("02_tefuri2","手振り2",5.87),("03_ukiuki","うきうき",0.33),("04_banzai","バンザイ(ジャンプ)",2.0),
 ("05_obie","怯え",None),("06_suneru","拗ねる",None),("07_akubi","あくび",5.2),("08_munekakushi","胸隠し",None),("09_suka-toOsae","スカート押さえ",2.5),
 ("10_orz","orz",None),("11_luckySUKEBE","ラッキーハプニング",0.83),("12_dojikko","ドジっ子",None),("13_tere1","照れ1",None),("14_tere2","照れ2",None),("15_kiss-stand","キス(立ち)",5.0),
 ("16_kiss-sit","キス(座り)",5.07),("17_sleep","スリープ",4.83),("18_umauma","ウマウマ(エモート)",0.42),("19_yoshi","ヨシ！",None),("20_fumituke","踏みつけ",3.97),
 ("21_kabeshiri-Head","壁に埋まる(頭側)",7.41),("21_kabeshiri-Hip","壁に埋まる(お尻側)",7.41)],
"エモート2":[("01_tefuri3","手振り3",3.1),("02_ojigi","おじぎ",2.1),("03_himitsu","ひみつ",None),("04_hawawa","はわわっ",4.15),("05_peace1","ピース1(エモート)",2.8),
 ("06_peace2","ピース2(エモート)",None),("07_gaow","がおー",1.43),("08_hyokkori","ひょっこり",9.37),("09_tyu2byou","厨二病",4.9),("10_takaPose","鷹のポーズ",None),
 ("11_DoNotStop","止まるんじゃねぇぞ…",None),("12_jikaiyokoku","次回予告",None),("13_angelTaisou","エンジェル体操",4.2),("14_zazen","座禅",None),
 ("15_sanpai","参拝",10.97),("16_shafudo","シャフ度",None),("17_kabedon1","壁ドン1",1.87),("18_kabedon2","壁ドン2",1.97),
 ("19-1_jankenG","じゃんけん(グー)",2.33),("19-2_jankenC","じゃんけん(チョキ)",2.33),("19-3_jankenP","じゃんけん(パー)",2.33),
 ("20_fusionL","フュージョン(左)",5.27),("20_fusionR","フュージョン(右)",5.27)],
"エモート3":[("01_MonkeyDance","モンキーダンス",0.18),("02_Justis","正義",3.7),
 ("03-1_KetsuWarp","尻ワープ",2.1),("03-2_KetsuWarpPlus","尻ワープ(＋着地)",13.5),("03-3_KetsuWarpShort","尻ワープ(短い)",1.6),
 ("04-1_Kakapo-Slow","カカポ(ゆっくり)",0.5),("04-2_Kakapo-Normal","カカポ",0.27),("04-3_Kakapo-Fast","カカポ(速い)",0.07),("05_Tefuri5","手振り5",0.27),
 ("06_CossackDance","コサックダンス",0.37),("07_AddChannel","チャンネル登録よろしく",0.77),("08_DonaldStep","ドナルドステップ",1.27),("09_RanRanRu-","RanRanRu-",None),
 ("10_Nattoru","なっとるやろがい！",1.3),("11_YGKR-Dance","YGKRダンス",10.53),("12_MRY-Step","MRYステップ",0.5),("13_CTC","CTC",None)],
"エモート: 別版(1ループ・SDK2)":[("01_tefuri1_OneLoop","手振り1(1ループ)",0.18),("02_tefuri2_OneLoop","手振り2(1ループ)",0.21),("03_ukiuki_OneLoop","うきうき(1ループ)",0.66),
 ("20_fumituke_OneLoop","踏みつけ(1ループ)",0.5),("01_tefuri3_OneLoop","手振り3(1ループ)",0.34),("12_jikaiyokoku_OneLoop","次回予告(1ループ)",1.49),
 ("13_angelTaisou_OneLoop_01","エンジェル体操(1ループ1)",0.21),("13_angelTaisou_OneLoop_02","エンジェル体操(1ループ2)",0.42),
 ("13_angelTaisou_OneLoop_03","エンジェル体操(1ループ3)",0.2),("13_angelTaisou_OneLoop_04","エンジェル体操(1ループ4)",0.27),
 ("05_Tefuri5_OneLoop","手振り5(1ループ)",0.23),("11_YGKR-Dance_OneLoop-1","YGKRダンス(1ループ1)",0.53),("11_YGKR-Dance_OneLoop-2","YGKRダンス(1ループ2)",0.53),
 ("11_YGKR-Dance_OneLoop-3","YGKRダンス(1ループ3)",0.53),("11_YGKR-Dance_OneLoop-4","YGKRダンス(1ループ4)",0.53),("11_YGKR-Dance_OneLoop-5","YGKRダンス(1ループ5)",0.5),
 ("13_CTC_OneLoop-1","CTC(1ループ1)",1.73),("13_CTC_OneLoop-2","CTC(1ループ2)",0.43),
 ("17_sleep_SDK2","スリープ(SDK2)",24.83),("18_umauma_SDK2","ウマウマ(SDK2)",0.43),("01_MonkeyDance_SKD2","モンキーダンス(SDK2)",6.67),
 ("04-1_Kakapo-Slow_SDK2","カカポ(ゆっくり・SDK2)",1.1),("04-2_Kakapo-Normal_SDK2","カカポ(SDK2)",0.57),("04-3_Kakapo-Fast_SDK2","カカポ(速い・SDK2)",7.4)],
}

# 英語の名前(pose_aliases.en.txt)。ファイル名(の印)→ 名前。ネタ系は元の題をローマ字にして、短い説明を付けたものもある。
EN={
"Idle":"Stand (basic)",
"010_0010":"Arms open","010_0020":"Hand on chest","010_0030":"Hugging own arm","010_0040":"Leaning peace sign",
"010_0050":"Hands on head and hip","010_0060":"Waving on one leg","010_0070":"Prim stance","010_0080":"Fists, one leg up",
"010_0090":"Hand on hip","010_0100":"Hands on cheeks","010_0110":"Arm stretched sideways","010_0120":"Hand up, one leg",
"010_0130":"Bouncy arms open","010_0140":"High kick","010_0150":"Hands clasped in front","010_0160":"Looking back, leaning",
"010_0170":"Arms open, firm stance","010_0180":"Arms out, one leg","010_0190":"Hand on head","010_0200":"Reaching, looking back",
"011_0010":"Sitting, legs out","011_0020":"W-sitting","011_0030":"Side sitting","011_0040":"Ear cleaning (giving)",
"011_0050":"Squatting, hands together","011_0060":"Sitting, hugging knees","011_0070":"Cross-legged","011_0080":"Sitting, one knee up",
"011_0090":"Squatting, one hand down","011_0100":"Kneeling, hand on head",
"012_0010":"Lying on back","012_0020":"On back, hand to mouth","012_0030":"Spread-eagle","012_0040":"Lying on side",
"012_0050":"Lying face down","012_0060":"Face down, legs up","012_0070":"Lying on one elbow","012_0080":"Sitting, leaning back",
"012_0090":"Curled up asleep","012_0100":"Asleep on arm",
"020_0010":"Arms crossed (male)","020_0020":"Scratching head","020_0030":"On guard","020_0040":"Pointing (male)",
"020_0050":"Hand on chin","020_0060":"Hands on hips (male)","020_0070":"Cross-legged (male)","020_0080":"Squatting low (yankee)",
"020_0090":"Lying, chin in hand","020_0100":"Fist raised",
"030_0010":"Seiza (formal sitting)","030_0020":"Dogeza (deep bow)","030_0030":"Striding","030_0040":"Kneeling in despair",
"030_0050":"Falling","030_0060":"Looking at phone","030_0061":"On the phone","030_0070":"Hand on wall",
"030_0080":"Sitting, tying hair","030_0090":"Stretching","030_0100":"Offering a hand",
"040_0010":"Yamcha down","040_0020":"Getdan","040_0030":"Change wiper","040_0040":"Tonfa kick",
"040_0050":"Inugami-ke (legs out of water)","040_0060":"Hattoushin 1","040_0070":"Hattoushin 2","040_0080":"Hattoushin 3","040_0090":"Running all out",
"040_0100":"Yaranaika?","040_0110":"Cock Robin","040_0120":"Saturday Night Fever","040_0130":"Daisuke",
"040_0140":"Homoo...","040_0150":"No time for bamboo","040_0160":"A-pose","040_0170":"Kabuki",
"040_0180":"Sogebu 1","040_0181":"Sogebu 2","040_0182":"Sogebu 3","040_0183":"Sogebu 4",
"040_0190":"Bodybuilding (front)","040_0191":"Bodybuilding (side chest)","040_0192":"Bodybuilding (side)",
"040_0200":"Special force: cream","040_0201":"Special force: butter","040_0202":"Special force: cheese","040_0203":"Special force: yogurt","040_0204":"Special force: milk",
"040_0210":"Umauma","040_0220":"Yoshi! (pointing check)","040_0270":"Don't stop","040_0350":"Get!","040_0360":"Kishida Mel",
"040_0370":"Foreign 4-koma 1","040_0371":"Foreign 4-koma 2","040_0372":"Foreign 4-koma 3","040_0373":"Foreign 4-koma 4","040_0374":"Foreign 4-koma 5",
"040_0380":"Weird old man","040_0390":"Slanting 1","040_0391":"Slanting 2","040_0400":"Bow-legged, arms open","040_0401":"Hands up, surrender",
"040_0410":"Let's go to heaven","040_0420":"JOChallenge","040_0430":"Just Do It!!",
"040_0440":"Donald dance 1","040_0441":"Donald dance 2","040_0442":"Donald dance 3","040_0443":"Donald dance 4","040_0444":"Donald dance 5",
"040_0450":"Unforgivable angle","040_0460":"What Christmas!","040_0470":"Kona banana","040_0480":"GB declaration","040_0490":"Alien x alien",
"040_0500":"Gonna tell the teacher 1","040_0501":"Gonna tell the teacher 2",
"040_0510":"Shoujo manga kung fu 1","040_0511":"Shoujo manga kung fu 2","040_0512":"Shoujo manga kung fu 3","040_0513":"Shoujo manga kung fu 4","040_0514":"Shoujo manga kung fu 5",
"040_0520":"Please forgive me!","040_0521":"Eeeei","040_0522":"Wei","040_0530":"Drone",
"040_0531":"New outfit reveal stream","040_0532":"VTuber leaving mid-stream",
"050_0010":"Jump, both hands up","050_0020":"Jump, waving","050_0030":"Jump, legs bent back","050_0040":"Jump, one hand up",
"050_0050":"Jump, hands at chest","050_0060":"Jump, hand on head","050_0070":"Jump, arms open","050_0080":"Jump, leg up",
"050_0090":"Jump, both hands up (male)","050_0100":"Jump, fist (male)","050_0110":"Running jump","050_0120":"Big leap (male)",
"050_0130":"Jump, leaning back","050_0140":"Hop on one leg","050_0150":"Jump in seiza","050_0160":"Dive",
"050_0170":"Cool light-magic pose","050_0180":"Breaking glass","050_0190":"Jump backward","050_0200":"Floating, arms open",
"000_IdleFinger":"Fingers (idle)","010_Fist01":"Fist 1","011_Fist02":"Fist 2","012_Fist03":"Fist 3",
"020_HandOpen01":"Open hand 1","021_HandOpen02":"Open hand 2","022_HandOpen03":"Open hand 3","023_HandOpen04":"Open hand 4",
"030_FingerPoint01":"Point","040_Victory01":"Peace 1","041_Victory02":"Peace 2","050_RocknRoll01":"Rock'n'roll 1","051_RocknRoll02":"Rock'n'roll 2",
"060_HandGun01":"Finger gun","070_ThumbsUp01":"Thumbs up","080_Kusukusu":"Giggle","090_Oku":"Hand resting","091_Tsukamu":"Grab",
"092_Tsumamu":"Pinch","093_Nigiru":"Grip","100_Heart01":"Heart (two hands)","110_Fox":"Fox","120_Money":"OK / money",
"130_Yubikiri":"Pinky promise","140_Jibeta":"Hand on ground","150_Patchn":"Finger snap","160_Wanawana":"Trembling",
"00_idle":"Idle (emote)","01_tefuri1":"Wave 1","02_tefuri2":"Wave 2","03_ukiuki":"Excited","04_banzai":"Banzai (jump)",
"05_obie":"Scared","06_suneru":"Sulking","07_akubi":"Yawn","08_munekakushi":"Covering chest","09_suka-toOsae":"Holding skirt down",
"10_orz":"orz","11_luckySUKEBE":"Lucky accident","12_dojikko":"Clumsy","13_tere1":"Shy 1","14_tere2":"Shy 2","15_kiss-stand":"Kiss (standing)",
"16_kiss-sit":"Kiss (sitting)","17_sleep":"Sleep","18_umauma":"Umauma (emote)","19_yoshi":"Yoshi!","20_fumituke":"Stomping",
"21_kabeshiri-Head":"Stuck in wall (head side)","21_kabeshiri-Hip":"Stuck in wall (hip side)",
"01_tefuri3":"Wave 3","02_ojigi":"Bow","03_himitsu":"Secret","04_hawawa":"Hawawa","05_peace1":"Peace 1 (emote)",
"06_peace2":"Peace 2 (emote)","07_gaow":"Rawr","08_hyokkori":"Peeking out","09_tyu2byou":"Chuunibyou","10_takaPose":"Hawk pose",
"11_DoNotStop":"Don't stop...","12_jikaiyokoku":"Next episode preview","13_angelTaisou":"Angel exercise","14_zazen":"Zazen",
"15_sanpai":"Shrine prayer","16_shafudo":"Shaft head tilt","17_kabedon1":"Kabedon 1","18_kabedon2":"Kabedon 2",
"19-1_jankenG":"Janken (rock)","19-2_jankenC":"Janken (scissors)","19-3_jankenP":"Janken (paper)",
"20_fusionL":"Fusion (left)","20_fusionR":"Fusion (right)",
"01_MonkeyDance":"Monkey dance","02_Justis":"Justice",
"03-1_KetsuWarp":"Butt warp","03-2_KetsuWarpPlus":"Butt warp (+ landing)","03-3_KetsuWarpShort":"Butt warp (short)",
"04-1_Kakapo-Slow":"Kakapo (slow)","04-2_Kakapo-Normal":"Kakapo","04-3_Kakapo-Fast":"Kakapo (fast)","05_Tefuri5":"Wave 5",
"06_CossackDance":"Cossack dance","07_AddChannel":"Please subscribe","08_DonaldStep":"Donald step","09_RanRanRu-":"RanRanRu-",
"10_Nattoru":"Nattoru yarogai!","11_YGKR-Dance":"YGKR dance","12_MRY-Step":"MRY step","13_CTC":"CTC",
}
EN_GROUP={"立ち":"Standing","座り":"Sitting","寝る・その他":"Lying & other","男性":"Male","日常":"Everyday","ネタ":"Memes","ジャンプ":"Jumping",
 "エモート1":"Emotes 1","エモート2":"Emotes 2","エモート3":"Emotes 3","エモート: 別版(1ループ・SDK2)":"Emotes: variants (one loop, SDK2)"}
def en_variant(f):
    # 別版: 元の名前 + (one loop N) / (SDK2)
    import re
    m=re.match(r"(.*?)_(OneLoop|SDK2|SKD2)(?:[_-](\d+))?$",f)
    base,kind,n=m.group(1),m.group(2),m.group(3)
    b=EN[base].replace(' (emote)','')
    if kind=="OneLoop": return f"{b} (one loop{' '+str(int(n)) if n else ''})"
    return f"{b} (SDK2)" if not b.endswith(")") else b[:-1]+", SDK2)"

En=len(sys.argv)>2 and sys.argv[2]=='--en'
L=[]
w=L.append
if En:
    w("# Pose aliases (read by the VRM Pose Viewer and the panel; the viewer's \"Register…\" and \"Edit…\" also rewrite this file)")
    w("#")
    w("# English users: rename this file to pose_aliases.txt (replacing the Japanese one).")
    w("#")
    w("# One per line:  name  kind  target  [time]")
    w("#   kind anim: target is a path to a .anim (relative to this file, or absolute). kind pose: the name of a pose saved in poses/")
    w("#   time: seconds (1.6), frame (f30) or end. Omitted = end")
    w("# Separated by spaces. Quote things containing spaces with \"...\". From # (outside quotes) to the end of line is a comment.")
    w("# A [group] line starts a group until the next [..]. Lines before the first [..] are the root (always visible).")
    w("#")
    w("# The animations in anim/nccy_*/ are from free assets by Necocoya (Kanna Hisashi).")
    w("# This file has only names and locations; get the assets and import them with the viewer's \"Import…\"")
    w("# (the sources are in anim_sources.txt; the downloaded zips can be imported as they are).")
    w("#   [Pose set]  https://necocoya.booth.pm/items/1634088  (NCCY_PoseSet_Free)")
    w("#   [Emote set] https://necocoya.booth.pm/items/1065089  (EmoteSet_Free)")
    w("# The names are translated from the Japanese example (the author's catalog titles and motion names). Many of the")
    w("# \"Memes\" are Japanese internet jokes; their names are just romanized. For emotes (moving animations), one moment is chosen.")
    w("# All animations of the two assets are registered (except an editing template). Delete what you don't use.")
    w("")
    w(f'"{EN["Idle"]}"  anim  {P}000_0000_Idle.anim')
else:
    w("# ポーズのエイリアス(VRM ポーズビューアーとパネルが読む。ビューアーの「登録…」「編集…」でも書き換えられる)")
    w("#")
    w("# 1 行に 1 つ:  エイリアス名  種類  対象  [時刻]")
    w("#   種類 anim: 対象は .anim のパス(このファイルの所からの相対か、絶対パス)。種類 pose: poses/ に保存したポーズの名前")
    w("#   時刻: 秒(1.6)、フレーム(f30)、最後(end)。省略すると最後")
    w("# 区切りは空白。空白を含むものは \"...\" で囲む。引用符の外の # から行末はコメント。")
    w("# [グループ名] の行から次の [..] までがそのグループ。最初の [..] より前はルート(いつも見えている所)。")
    w("#")
    w("# ここにある anim/nccy_*/ のアニメーションは、ねここや(かんな久)さんの無料のアセットのものです。")
    w("# このファイルには名前と場所しか入っていないので、使う前にアセットを入手して、ビューアーの「取り込み…」で取り込んでください")
    w("# (取り込み元は anim_sources.txt。入手した zip のままで取り込めます)。")
    w("#   [ポーズ詰め合わせ] アンテナショップねここや  https://necocoya.booth.pm/items/1634088  (NCCY_PoseSet_Free)")
    w("#   [Emote詰め合わせ] アンテナショップねここや  https://necocoya.booth.pm/items/1065089  (EmoteSet_Free)")
    w("# エイリアス名は、作者のカタログの題(ネタ系など)と説明書の動作名を使い、題のないものは見た目から付けました。")
    w("# エモート(動きのあるアニメーション)は、動きの中の 1 つの時刻を選んであります。一部の名前は言い換えました")
    w("# (ケツワープ → 尻ワープ、壁尻 → 壁に埋まる、ラッキースケベ → ラッキーハプニング)。")
    w("# 2 つのアセットのアニメーションは(改変用テンプレートの待機を除いて)すべて登録してあります。使わないものは消してかまいません。")
    w("")
    w(f'立ち(基本)  anim  {P}000_0000_Idle.anim')

def q(n): return f'"{n}"' if (' ' in n or '#' in n) else n
for g,items in poses.items():
    w(""); w(f"[{'Pose: '+EN_GROUP[g] if En else 'ポーズ: '+g}]")
    for f,n in items: w(f'{q(EN[f] if En else n)}  anim  {P}{f}.anim')
for side,sfx,jp in (("Right","Right","右手"),("Left","Left","左手"),("Both","Both","両手")):
    w("")
    if En: w(f"[Hand sign: {side.lower() if side!='Both' else 'both'}]   # fingers only. In the panel and OscPoser they are layered on the current pose (the viewer shows the fingers alone)")
    else: w(f"[ハンドサイン: {jp}]   # 指だけのアニメーション。パネル・OscPoser では今のポーズの指に重なる(ビューアーでは指だけの形に見える)")
    for f,n in hands: w(f'{q(EN[f]+" ("+side.lower()+")" if En else n+"("+jp+")")}  anim  {P}700_{f}-{sfx}.anim')
for g,items in emotes.items():
    w(""); w(f"[{EN_GROUP[g] if En else g}]")
    for f,n,t in items:
        nm=(EN[f] if f in EN else en_variant(f)) if En else n
        w(f'{q(nm)}  anim  {E}{f}.anim' + (f'  {t}' if t is not None else ''))
open(sys.argv[1],'w',encoding='utf-8').write("\n".join(L)+"\n")
import shlex; names=[shlex.split(l)[0] for l in L if l and not l.startswith(('#','['))]
import collections; print(len(names),[k for k,v in collections.Counter(names).items() if v>1])
