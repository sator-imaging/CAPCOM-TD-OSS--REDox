# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE
# THIS DOCUMENT IS OUT OF DATE



























































- Quote モードは、文字列(キーを含む)を常にダブルクォートで書き出します。計画の「型が衝突する場合のみクォート」とは異なります。JSON との互換性は高いですが、読みやすさでは劣ります。意図した仕様か確認してください。
- MaxDepth の判定は、計画では Array / Map が担う想定でしたが、実装は ParseNode(YamlDocument.cs)にあります。
- ParseNode / ParseScalarNode / StoreScalar / CompleteContainer は YamlDocument.cs にあります。振り分けの範囲なので、計画の「型に属さない処理」と矛盾はありません。

不具合の可能性が高い箇所

1. >(折りたたみ)の空行(YamlDocument.String.cs の ParseBlockString)
   - a、空行、b を a\n\nb にしています。
   - YAML では a\nb になります。空行1つが \n 1つに対応するためです。
   - インデントが深い行(more-indented)の扱いもありません。
2. 空の | / >: 内容が空でも、既定の clip で "\n" になります。正しくは "" です。
3. ルート直下の |: ParseNode(0) の parentIndent が 0 のため、インデント 0 の内容を受け付けません。
4. TryParse の取りこぼし: 不正な UTF-8 は new UTF8Encoding(false, true) の例外になり、DocumentParseException ではないので、TryParse が false を返さず例外を投げます。
5. ダブルクォート内の改行: 空行を \n にせず、すべてスペースにします。
6. a: b: c: 1行に2つのマップが書かれたものを、入れ子のマップとして受け付けます。YAML では不正です。

品質・効率の指摘

- WriteString が、文字列ごとに TextEncoderPolicy と GetEncoder を作り、EscapeRanges.ToArray() まで行います。Write 開始時に1度だけ作ってください。
- Duplicate() は MemberwiseClone なので、_scalarValues と _trailingTrivia を元の文書と共有します。クローン側でトークンを編集して文字列を追加すると、元の文書にも影響します。
- 各スカラーを個別の byte[] に保存し、_text も全文コピーします。メモリが大きくなります。



<!-- FOLLOWINGS ARE DONE -->



# YAML 形式の基本対応

## 概要

`experimental/REDox.Yaml` 内で、null、真偽値、数値、文字列、配列、マップの解析と出力を実装する。JSON の値を YAML に変換して再解析できることを受け入れ条件とする。

## 実装

### 既存コードの扱い

- `experimental/REDox.Yaml` に簡易スケルトンがある（`REDox.slnx` 登録済み、`REDox.csproj` に `InternalsVisibleTo REDox.Yaml` 設定済み）。パーサは引用符・`|`・`>`・エスケープ・型判定が未対応の簡易実装のため**作り直す**。書き出しは構造部分のみ流用し、文字列の出力を差し替える。
- `Parse` の `settings` は TOML と同じ `SerializerSettings? settings = null` に揃える。

### ファイル構成

- `YamlDocument` を `partial` 宣言にする。型に属する処理は、解析（Deserialize）・書き出し（Serialize）・`Decode*` のオーバーライドを含め、**すべて対象の型ごとのファイルに置く**。単純な型も例外にせず、型の大小にかかわらず完全に分ける。解析と書き出しで別ファイルにしたり、型によって分けたり分けなかったりしない。
  - `YamlDocument.Null.cs`: null（`null`・`~`・空）の解析、出力、`Decode*`
  - `YamlDocument.Boolean.cs`: 真偽値の解析、出力、`Decode*`
  - `YamlDocument.Number.cs`: 整数・浮動小数（`.inf`・`.nan` を含む）の解析、出力、`DecodeInteger`・`DecodeFloat`
  - `YamlDocument.String.cs`: プレーン・引用・`|`・`>`・chomping の解析、`Quote`・`Multiline` の出力、`DecodeUtf8Bytes`
  - `YamlDocument.Array.cs`: ブロック／フロー配列の解析と出力
  - `YamlDocument.Map.cs`: ブロック／フローマップの解析と出力
  - `YamlDocument.Trivia.cs`: コメント・空白の解析、`PreserveTrivia` 時の出力、`DecodeTrivia`
  - `YamlDocument.ParseException.cs`: `ParseException` とエラーコード
- `YamlDocument.cs` には型に属さない処理だけを置く。具体的には、公開の `Parse`・`TryParse`・`Encode*` の入口、`Duplicate`、ソース保持、そして種別ごとの Deserialize／Serialize へ振り分ける呼び出しである。
- プレーンスカラーの型決定（null・真偽値・数値・文字列のどれか）の順序は振り分けで決め、各型の判定本体は各ファイルの Deserialize に置く。インデントやフローの深さ（`MaxDepth`）の管理は `Array`・`Map` の各ファイルが担う。

### 解析

- ブロック形式とフロー形式の配列・マップ、基本型、引用・非引用文字列、`|`・`>` および `+`・`-`（インデント指示子を含む）を扱う。
- 対象外のアンカー、エイリアス、タグ、複数文書、複雑キー、ディレクティブは解析エラーにする。
- コメントは `PreserveTrivia` 時に `TriviaLineComment`、空白は `TriviaWhitespace` として保持する（TOML と同じ扱い）。

### オプション

- `YamlDocumentOptions`（`readonly record struct`、namespace は `REDox.Yaml`）に `EnableValueValidation`、`PreserveTrivia`、`MaxDepth`、`AutoDetectEncoding`、`EnableFrontMatter` を用意する。front matter は TODO を付けて公開のみに留め、誤記の `NewLinLoadFrontMatter` は追加しない。
- `YamlWriteOptions` に `YamlStringMode`（`Quote`、`Multiline`。新規 enum）と、末尾改行を「保持（既定）／削除／1個に揃える」から選ぶ `NormalizeStringNewlineAtEnd` を追加する。正規化は書き出すすべての文字列値に適用する。改行・インデントには既存の `Utf8TextWriteOptions` を使う。

### 書き出し

以下の文字列に関する規則は `YamlDocument.String.cs` に実装する。引用符を省略できるかの判定は、独立した static クラス `YamlStringHelper`（`CanWritePlain(value, isKey)`）に置き、キーにも値にも同じ規則を適用する。null・真偽値・数値に読めるかの判定は、各型のファイル（`Null.cs`・`Boolean.cs`・`Number.cs`）の静的メソッドを `YamlStringHelper` が呼ぶ。

- `Quote`: 1行形式で書き、引用符は必要なときだけ付ける（下記の `YamlStringHelper` の判定）。引用・エスケープには既存の `src/REDox/Json/JsonTextEncoder.cs` を使い、自前実装しない。JSON のエスケープは YAML のダブルクォートと互換。U+007F は YAML で非印字文字のため `TextEscapeMask.Delete` を加えて `\u007f` にする（policy を調整できるか実装時に確認）。孤立サロゲートは YAML で表現できないため往復テストの除外対象とする。
- `Multiline`: 表現できない場合を除き、改行の有無にかかわらず**常に** `|` 系（`|`・`|-`・`|+`、必要なら `|2` 等）で出力する。これ以外の例外は設けない。`|` で表現できない内容（制御文字など）は `Quote` へフォールバックせず、**例外を投げて書き出しを失敗させる**。この扱いは `Multiline` にのみ適用し、`Quote` は常に書き出せる。`>` 系は解析のみ対応。`Quote` 出力は常に1行。
- TODO: 改行を含む文字列だけ `|`、それ以外は引用にする `Mixed` モードを `YamlStringMode` に追加する。今回は実装せず、コードにも TODO コメントを残す。
- 引用符を省略できるのは、`YamlStringHelper.CanWritePlain` が真を返す文字列だけとする。判定は保守的で、迷ったら引用符を付ける。次のどれかに当てはまる文字列は引用符を付ける。
  - 空、前後が空白、先頭が YAML の指示文字（`-?:,[]{}#&*!|>'"%@` とバッククォート）、先頭が `.`・`+`・数字
  - 改行・タブ・制御文字・DEL・C1 制御文字・行区切り・BOM・非文字、対になっていないサロゲートを含む
  - `: `（コロンの後が空白・末尾・`,`・`]`・`}`）や ` #` を含む
  - キーの場合は、さらに `:`・`#`・引用符を1文字でも含む（パーサがキーの終端を探すため）
  - null・真偽値・数値に読める文字列、および YAML 1.1 で真偽値に読まれる `y`・`n`・`yes`・`no`・`on`・`off`、`=`、`<<`
  これにより `"true"`・`"123"`・`"null"` のような文字列も型が変わらない。
- 空文字列は `""`、空コンテナは `[]`・`{}` とする。`Multiline` のキーにも同じ判定を使う。

## 検証

テストは `tests/REDox.Yaml.Tests/` を新設する（xUnit v3、`REDox.slnx` の `/tests/` に登録、`REDox.Toml.Tests` を雛形にする）。スイートの探索は `TomlParseTest` の `FindTestSuiteDirectory`（実行ディレクトリから親を辿って `external/...` を探す）と同じ方式にする。

- **JSON 往復テスト**を独立させ、`external/JSONTestSuite` の有効な JSON（`y_*.json`）について、`JsonDocument.Parse` → `YamlDocument.Encode`（`Quote`・`Multiline` の両モード）→ `YamlDocument.Parse` → 値の一致を確認する。`Multiline` で表現できない文字列を含むケースは、値の一致ではなく書き出しが例外になることを期待する（`Quote` では一致を必須とする）。数値は表現差（`1.0`、`1e5`、`-0`、巨大整数）を許容する比較にする。`n_`・`i_` は例外が出てもよいが、クラッシュしないことだけ確認する。除外するケースは理由付きの一覧で管理する。
- **YAML 自体のテスト**は、型ごとのファイル構成に対応させて次の塊に分け、塊ごとにテストクラスを作る。
  - `YamlBasicPrimitivesTest`: null、真偽値、整数、浮動小数（`.inf`・`.nan`、型が衝突する値）
  - `YamlStringsTest`: プレーン・シングル／ダブルクォートとエスケープ、`|`・`>` と chomping・末尾改行、`YamlStringMode`・`NormalizeStringNewlineAtEnd`、引用符の要否（`YamlStringHelper` の判定）と型が衝突する文字列の往復（正常系のみ）
  - `YamlArrayTest`: ブロック／フロー配列、入れ子、空配列
  - `YamlMapTest`: ブロック／フローマップ、入れ子、空マップ、配列との混在
  - `YamlTriviaTest`: コメント・空白と `PreserveTrivia`
  - `YamlOptionsTest`: `EnableValueValidation`、`AutoDetectEncoding` ほか、型をまたぐオプションの正常系
  - **意図的にエラーになるテストは、上記の正常系とは別ファイルに分け、1つの塊にまとめる**: `YamlErrorTest`。含めるもの: `Multiline` で表現できない文字列の書き出しが例外になること（`Quote` は例外にならないことも対で確認）、`MaxDepth` 超過、対象外機能（アンカー、エイリアス、タグ、複数文書、ディレクティブ）と不正な構文の解析エラー、`EnableValueValidation` による不正値の拒否。正常系のテストクラスには例外を期待するテストを置かない。
- [yaml-test-suite](https://github.com/yaml/yaml-test-suite) を `external/yaml-test-suite` にサブモジュールとして追加し、`data-2022-01-17` タグのコミットに固定する。この名前はブランチではなくタグなので、`git submodule add -b` は使わない。YAML 自体のテストで、今回の対応範囲に該当するケースを許可リストで選び、付属の期待 JSON 値と比較する。[データ版の構成](https://github.com/yaml/yaml-test-suite#the-data-branch-files)に従い複数のサブケースも扱い、`error` を持つケースは解析エラーを期待する。許可リストは対応範囲の拡大に合わせて広げる。
- 固定したタグでは各テスト ID のディレクトリがサブモジュール直下にあり、複数のサブケースはその下の番号付きディレクトリにある。
- `external/` はサブモジュール未初期化の環境では空なので、実行前に `git submodule update --init` が必要。テストの実行は実装後に行う。
- 併せて README の submodule の説明、サードパーティのライセンス表、パッケージ表（YAML は Experimental）を更新する。

## 前提

- YAML プロジェクトは `experimental/` に残す。
- `YamlStringMode.Multiline` の出力は `|` 系に統一し、表現できない場合以外は常にこの形式で書き、表現できない場合は例外とする（`Multiline` のみ）。`Mixed` モードは TODO とする。
- 対象は基本型、各種文字列形式、配列、マップに限り、YAML の全機能は対応しない。
- front matter の解析は今回実装しない。既定の末尾改行設定は「保持」とする。
