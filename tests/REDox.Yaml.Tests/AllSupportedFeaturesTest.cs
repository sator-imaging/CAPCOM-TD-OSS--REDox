using System;
using REDox.Cbor;
using REDox.Json;

namespace REDox.Yaml.Tests;

public sealed class AllSupportedFeaturesTest
{
    [Fact]
    public void GitHubActionsWorkflow_RoundTripsAndPreservesValues()
    {
        var github = """
                     name: Node.js CI

                     on:
                       push:
                         branches: [ main, dev ]

                     jobs:
                       build:
                         runs-on: ubuntu-latest
                         steps:
                           - uses: actions/checkout@v4
                           - uses: actions/setup-node@v4
                             with:
                               node-version: 18
                           - run: npm ci
                           - run: npm test
                     """;

        using var sourceYamlDocument = YamlDocument.Parse(
            github,
            options: new YamlDocumentOptions()
            {
                // Prevent reading 'on' as a boolean
                ReadMapKeyAsString = true,
            }
        );

        var serializedYaml = YamlDocument.EncodeToString(
            sourceYamlDocument.RootElement,
            options: new YamlWriteOptions()
            {
                IndentSize = 2,
                StringStyle = YamlStringStyle.Multiline,
            }
        );

        //// TODO: as-is roundtrip (need to keep original representation state (quote, list style, etc.)
        //Assert.Equal(githubWorkflowSample.ReplaceLineEndings(), serializedYaml.ReplaceLineEndings());

        using var restoredYamlDocument = YamlDocument.Parse(serializedYaml);

        Assert.True(
            DValue.DeepEquals(
                sourceYamlDocument.RootElement.AsValue(),
                restoredYamlDocument.RootElement.AsValue()
            ),
            $"GitHub workflow data changed after writing and reading back to YAML:\n{serializedYaml}"
        );

        Assert.True(
            DValue.DeepEquals(
                CborDocument
                    .Parse(CborDocument.Encode(restoredYamlDocument.RootElement))
                    .RootElement.AsValue(),
                restoredYamlDocument.RootElement.AsValue()
            ),
            $"GitHub workflow CBOR exchange failed:\n{serializedYaml}"
        );

        Assert.True(
            DValue.DeepEquals(
                JsonDocument
                    .Parse(JsonDocument.Encode(restoredYamlDocument.RootElement))
                    .RootElement.AsValue(),
                restoredYamlDocument.RootElement.AsValue()
            ),
            $"GitHub workflow JSON exchange failed:\n{serializedYaml}"
        );

        var workflow = restoredYamlDocument.RootElement.AsValue();

        // xUnit2000 asks for the constant as the first argument. Suppress it for readability.
#pragma warning disable xUnit2000
        Assert.Equal(workflow.Count, 3);
        Assert.Equal(workflow["name"].AsString(), "Node.js CI");
        Assert.Equal(workflow["on"].Count, 1);
        Assert.Equal(workflow["on"]["push"].Count, 1);
        Assert.Equal(workflow["on"]["push"]["branches"].Count, 2);
        Assert.Equal(workflow["on"]["push"]["branches"][0].AsString(), "main");
        Assert.Equal(workflow["on"]["push"]["branches"][1].AsString(), "dev");
        Assert.Equal(workflow["jobs"].Count, 1);
        Assert.Equal(workflow["jobs"]["build"].Count, 2);
        Assert.Equal(workflow["jobs"]["build"]["runs-on"].AsString(), "ubuntu-latest");

        var steps = workflow["jobs"]["build"]["steps"];

        Assert.Equal(steps.Count, 4);
        Assert.Equal(steps[0]["uses"].AsString(), "actions/checkout@v4");
        Assert.Equal(steps[1]["uses"].AsString(), "actions/setup-node@v4");
        Assert.Equal(steps[1]["with"].Count, 1);
        Assert.Equal(steps[1]["with"]["node-version"].AsInt32(), 18);
        Assert.Equal(steps[2]["run"].AsString(), "npm ci");
        Assert.Equal(steps[3]["run"].AsString(), "npm test");
#pragma warning restore xUnit2000
    }

    [Fact]
    public void EverySupportedSyntaxAndLiteral_RoundTripsAndMatchesExpectedValues()
    {
        var yaml = """
                   # A comment on its own line.
                   comment: value # A comment at the end of a line.

                   nulls:
                     tilde: ~
                     lower: null
                     title: Null
                     upper: NULL
                     empty:
                   booleans:
                     - true
                     - True
                     - TRUE
                     - false
                     - False
                     - FALSE
                   integers:
                     zero: 0
                     positive: +12
                     negative: -12
                     largest: 9223372036854775807
                     smallest: -9223372036854775808
                     unsigned: 18446744073709551615
                     big: 123456789012345678901234567890
                     leading_zero_is_decimal: 0755
                     hexadecimal: 0x1F
                     hexadecimal_upper: 0xFF
                     octal: 0o17
                     binary: 0b1010
                     separators: 1_000_000
                     hexadecimal_separators: 0xDEAD_BEEF
                     binary_separators: 0b1010_0101
                   floats:
                     decimal: 3.14
                     negative: -0.5
                     explicit_plus: +1.5
                     no_integer_part: .5
                     no_fraction: 5.
                     exponent: 1e3
                     exponent_upper: 1.5E-3
                     signed_exponent: 2.5e+10
                     separators: 1_000.5
                     infinity: .inf
                     infinity_title: .Inf
                     infinity_upper: .INF
                     positive_infinity: +.inf
                     negative_infinity: -.inf
                     negative_infinity_title: -.Inf
                     negative_infinity_upper: -.INF
                     # Left out: DeepEquals compares floats with ==, so a NaN is never equal to itself. This aggregate
                     # round trip therefore cannot check NaN; the dedicated special-float tests cover its parsing and writing.
                     # not_a_number: .nan
                     # not_a_number_title: .NaN
                     # not_a_number_upper: .NAN
                     beyond_double: 1e400
                   timestamps:
                     date: 2001-12-14
                     utc: 2001-12-14T21:59:43Z
                     fraction_and_offset: 2001-12-14t21:59:43.10-05:00
                     spaces_and_short_offset: 2001-12-14 21:59:43.10 -5
                     positive_offset: 2001-12-14T21:59:43+09:30
                     no_offset: 2001-12-14 21:59:43
                   strings:
                     plain: hello world
                     plain_colon: a:b
                     plain_hash: a#b
                     plain_url: http://inter.net/path?query=1#fragment
                     plain_version: 1.2.3
                     plain_time: 12:30:45
                     plain_separator_edges: [1_, _1, 1__0, 0x]
                     yaml_1_1_words: ["y", "n", "yes", "no", "on", "off"]
                     # A more-indented plain-scalar continuation is valid and folds to a space.
                     # https://yaml.org/spec/1.2.2/#733-plain-style
                     plain_multiline: first line
                       second line
                         third line

                       after a blank line
                     single: 'it''s'
                     single_number: '123'
                     single_boolean: 'true'
                     single_null: 'null'
                     single_empty: ''
                     single_multiline: 'first line
                       second line'
                     double: "tab\there"
                     double_quotes: "say \"hi\""
                     double_number: "123"
                     double_empty: ""
                     double_control_escapes: "null \0 bell \a backspace \b vertical tab \v form feed \f return \r escape \e next line \N hex \x01"
                     double_escapes: "backslash \\ slash \/ tab \t newline \n space\ x no-break \_ hex \x41 unicode é wide \U0001F600 pair 😀 line \L paragraph \P"
                     double_continuation: "one \
                       two"
                     double_multiline: "first line
                       second line

                       after a blank line"
                     literal: |
                       line one
                         indented line
                       line three
                     literal_strip: |-
                       no trailing line break
                     literal_keep: |+
                       keeps the trailing line breaks

                     literal_indicator_1: |1
                      No leading space left
                     literal_indicator_3: |3
                          2 leading spaces left
                     literal_indicator_5: |5-
                          5 indent spaces gone
                     folded: >
                       folded text
                       continues here

                       new paragraph
                     folded_strip: >-
                       no trailing line break
                     folded_keep: >+
                       keeps the trailing line breaks

                     non_ascii: 日本語 😀
                   sequences:
                     - plain
                     - - nested
                       - sequence
                     - key: in a sequence
                       other: value
                     - [flow, sequence]
                     - {flow: map}
                     - 1
                     - null
                     # A bare "-" may put its mapping on the next, more indented line.
                     # https://yaml.org/spec/1.2.2/#821-block-sequences
                     -
                       key: in a sequence
                       other: value
                   sequence_at_the_indent_of_its_key:
                   - a
                   - b
                   flow:
                     sequence: [1, "two", 'three', [4], {five: 5}]
                     empty_sequence: []
                     empty_map: {}
                     map: {a: 1, "b": 2, 'c': 3, d: [x, "y"]}
                     multiline: [
                       one,
                       two
                       ]
                     trailing_comma: [1, 2,]
                     explicit_key: {? a : b}
                   keys:
                     1: integer key
                     true: boolean key
                     1.5: float key
                     ~: null key
                     2001-12-14: timestamp key
                     "quoted key": value
                     'single quoted key': value
                     ? explicit key
                     : explicit value
                     ? |
                       block scalar key
                     : value of the block scalar key
                   nesting:
                     map_in_sequence_in_map:
                       - name: first
                         tags:
                           - a
                           - b: [1, 2, {c: d}]
                         details:
                           level: 1
                           deeper:
                             level: 2
                             deepest:
                               level: 3
                               values: [1, [2, [3, [4]]]]
                       - name: second
                         empty_sequence: []
                         empty_map: {}
                         null_value:
                     sequence_of_sequences:
                       - - 1
                         - - 2
                           - - 3
                             - - 4
                       - - - - deep
                     flow_nested: {a: [1, {b: [2, {c: [3, {d: [4]}]}]}], e: "f"}
                     block_scalars_nested:
                       - text: |
                           literal in a map in a sequence
                         more:
                           - >
                             folded
                             in a sequence in a map
                           - plain after the folded scalar
                     non_string_keys_nested:
                       1:
                         true:
                           2001-12-14:
                             - 1.5: float key in a map in a sequence
                   """;

        using var document = YamlDocument.Parse(yaml);

        var writtenYaml = YamlDocument.EncodeToString(document.RootElement);

        using var restored = YamlDocument.Parse(writtenYaml);

        Assert.True(
            DValue.DeepEquals(document.RootElement.AsValue(), restored.RootElement.AsValue()),
            $"The document read back is not equal. YAML:\n{writtenYaml}"
        );

        /*
        // TODO: Values differ at $.timestamps.no_offset:
        //         expected TimestampOffsetDateTime payload: A00000364 (length: 20 , offset: 868),
        //           actual TimestampLocalDateTime payload: 98000034E (length: 19 , offset: 846).
        YamlTestFixture.DebugDeepEquals(
            CborDocument
                .Parse(CborDocument.Encode(restored.RootElement))
                .RootElement.AsValue(),
            restored.RootElement.AsValue()
        );
        */

        // xUnit2000 asks for the constant as the first argument. Suppress it for readability.
#pragma warning disable xUnit2000
        var root = restored.RootElement.AsValue();

        Assert.Equal(root["comment"].AsString(), "value");

        Assert.Equal(root["nulls"].Count, 5);
        Assert.Equal(root["nulls"]["tilde"].AsTokenKind(), DTokenKind.Null);
        Assert.Equal(root["nulls"]["lower"].AsTokenKind(), DTokenKind.Null);
        Assert.Equal(root["nulls"]["title"].AsTokenKind(), DTokenKind.Null);
        Assert.Equal(root["nulls"]["upper"].AsTokenKind(), DTokenKind.Null);
        Assert.Equal(root["nulls"]["empty"].AsTokenKind(), DTokenKind.Null);

        Assert.Equal(root["booleans"].Count, 6);
        Assert.Equal(root["booleans"][0].AsBoolean(), true);
        Assert.Equal(root["booleans"][1].AsBoolean(), true);
        Assert.Equal(root["booleans"][2].AsBoolean(), true);
        Assert.Equal(root["booleans"][3].AsBoolean(), false);
        Assert.Equal(root["booleans"][4].AsBoolean(), false);
        Assert.Equal(root["booleans"][5].AsBoolean(), false);

        Assert.Equal(root["integers"]["zero"].AsInt64(), 0);
        Assert.Equal(root["integers"]["positive"].AsInt64(), 12);
        Assert.Equal(root["integers"]["negative"].AsInt64(), -12);
        Assert.Equal(root["integers"]["largest"].AsInt64(), long.MaxValue);
        Assert.Equal(root["integers"]["smallest"].AsInt64(), long.MinValue);
        Assert.Equal(root["integers"]["unsigned"].AsUInt64(), ulong.MaxValue);
        Assert.Equal(root["integers"]["big"].AsTokenKind(), DTokenKind.BigNumber);
        Assert.Equal(root["integers"]["leading_zero_is_decimal"].AsInt64(), 755);
        Assert.Equal(root["integers"]["hexadecimal"].AsInt64(), 0x1F);
        Assert.Equal(root["integers"]["hexadecimal_upper"].AsInt64(), 0xFF);
        Assert.Equal(root["integers"]["octal"].AsInt64(), 15);
        Assert.Equal(root["integers"]["binary"].AsInt64(), 0b1010);
        Assert.Equal(root["integers"]["separators"].AsInt64(), 1_000_000);
        Assert.Equal(root["integers"]["hexadecimal_separators"].AsInt64(), 0xDEAD_BEEF);
        Assert.Equal(root["integers"]["binary_separators"].AsInt64(), 0b1010_0101);

        Assert.Equal(root["floats"]["decimal"].AsDouble(), 3.14);
        Assert.Equal(root["floats"]["negative"].AsDouble(), -0.5);
        Assert.Equal(root["floats"]["explicit_plus"].AsDouble(), 1.5);
        Assert.Equal(root["floats"]["no_integer_part"].AsDouble(), 0.5);
        Assert.Equal(root["floats"]["no_fraction"].AsDouble(), 5.0);
        Assert.Equal(root["floats"]["exponent"].AsDouble(), 1e3);
        Assert.Equal(root["floats"]["exponent_upper"].AsDouble(), 1.5E-3);
        Assert.Equal(root["floats"]["signed_exponent"].AsDouble(), 2.5e+10);
        Assert.Equal(root["floats"]["separators"].AsDouble(), 1_000.5);
        Assert.Equal(root["floats"]["infinity"].AsDouble(), double.PositiveInfinity);
        Assert.Equal(root["floats"]["infinity_title"].AsDouble(), double.PositiveInfinity);
        Assert.Equal(root["floats"]["infinity_upper"].AsDouble(), double.PositiveInfinity);
        Assert.Equal(root["floats"]["positive_infinity"].AsDouble(), double.PositiveInfinity);
        Assert.Equal(root["floats"]["negative_infinity"].AsDouble(), double.NegativeInfinity);
        Assert.Equal(root["floats"]["negative_infinity_title"].AsDouble(), double.NegativeInfinity);
        Assert.Equal(root["floats"]["negative_infinity_upper"].AsDouble(), double.NegativeInfinity);

        // NaN is not deep equatable

        Assert.Equal(root["floats"]["beyond_double"].AsDouble(), double.PositiveInfinity);

        // A timestamp without an offset is in UTC.
        Assert.Equal(
            root["timestamps"]["date"].AsDateTimeOffset(),
            new DateTimeOffset(2001, 12, 14, 0, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal(
            root["timestamps"]["utc"].AsDateTimeOffset(),
            new DateTimeOffset(2001, 12, 14, 21, 59, 43, TimeSpan.Zero)
        );

        Assert.Equal(
            root["timestamps"]["fraction_and_offset"].AsDateTimeOffset(),
            new DateTimeOffset(2001, 12, 14, 21, 59, 43, 100, TimeSpan.FromHours(-5))
        );

        Assert.Equal(
            root["timestamps"]["spaces_and_short_offset"].AsDateTimeOffset(),
            new DateTimeOffset(2001, 12, 14, 21, 59, 43, 100, TimeSpan.FromHours(-5))
        );

        Assert.Equal(
            root["timestamps"]["positive_offset"].AsDateTimeOffset(),
            new DateTimeOffset(2001, 12, 14, 21, 59, 43, new TimeSpan(9, 30, 0))
        );

        Assert.Equal(
            root["timestamps"]["no_offset"].AsDateTimeOffset(),
            new DateTimeOffset(2001, 12, 14, 21, 59, 43, TimeSpan.Zero)
        );

        Assert.Equal(root["strings"]["plain"].AsString(), "hello world");
        Assert.Equal(root["strings"]["plain_colon"].AsString(), "a:b");
        Assert.Equal(root["strings"]["plain_hash"].AsString(), "a#b");

        Assert.Equal(
            root["strings"]["plain_url"].AsString(),
            "http://inter.net/path?query=1#fragment"
        );

        Assert.Equal(root["strings"]["plain_version"].AsString(), "1.2.3");
        Assert.Equal(root["strings"]["plain_time"].AsString(), "12:30:45");

        Assert.Equal(root["strings"]["plain_separator_edges"][0].AsString(), "1_");
        Assert.Equal(root["strings"]["plain_separator_edges"][1].AsString(), "_1");
        Assert.Equal(root["strings"]["plain_separator_edges"][2].AsString(), "1__0");
        Assert.Equal(root["strings"]["plain_separator_edges"][3].AsString(), "0x");
        Assert.Equal(root["strings"]["yaml_1_1_words"][0].AsString(), "y");
        Assert.Equal(root["strings"]["yaml_1_1_words"][1].AsString(), "n");
        Assert.Equal(root["strings"]["yaml_1_1_words"][2].AsString(), "yes");
        Assert.Equal(root["strings"]["yaml_1_1_words"][3].AsString(), "no");
        Assert.Equal(root["strings"]["yaml_1_1_words"][4].AsString(), "on");
        Assert.Equal(root["strings"]["yaml_1_1_words"][5].AsString(), "off");
        Assert.Equal(
            root["strings"]["plain_multiline"].AsString(),
            "first line second line third line\nafter a blank line"
        );

        Assert.Equal(root["strings"]["single"].AsString(), "it's");
        Assert.Equal(root["strings"]["single_number"].AsString(), "123");
        Assert.Equal(root["strings"]["single_boolean"].AsString(), "true");
        Assert.Equal(root["strings"]["single_null"].AsString(), "null");
        Assert.Equal(root["strings"]["single_empty"].AsString(), "");
        Assert.Equal(root["strings"]["single_multiline"].AsString(), "first line second line");
        Assert.Equal(root["strings"]["double"].AsString(), "tab\there");
        Assert.Equal(root["strings"]["double_quotes"].AsString(), "say \"hi\"");
        Assert.Equal(root["strings"]["double_number"].AsString(), "123");
        Assert.Equal(root["strings"]["double_empty"].AsString(), "");
        Assert.Equal(
            root["strings"]["double_control_escapes"].AsString(),
            "null \0 bell \a backspace \b vertical tab \v form feed \f return \r escape \x1b next line \x85 hex \x01"
        );

        Assert.Equal(
            root["strings"]["double_escapes"].AsString(),
            "backslash \\ slash / tab \t newline \n space x no-break \xA0 hex A unicode \xE9 wide \U0001F600 pair \U0001F600 line \x2028 paragraph \x2029"
        );

        Assert.Equal(root["strings"]["double_continuation"].AsString(), "one two");
        Assert.Equal(
            root["strings"]["double_multiline"].AsString(),
            "first line second line\nafter a blank line"
        );

        Assert.Equal(
            root["strings"]["literal"].AsString(),
            "line one\n  indented line\nline three\n"
        );

        Assert.Equal(root["strings"]["literal_strip"].AsString(), "no trailing line break");
        Assert.Equal(
            root["strings"]["literal_keep"].AsString(),
            "keeps the trailing line breaks\n\n"
        );

        Assert.Equal(root["strings"]["literal_indicator_1"].AsString(), "No leading space left\n");
        Assert.Equal(
            root["strings"]["literal_indicator_3"].AsString(),
            "  2 leading spaces left\n"
        );

        Assert.Equal(root["strings"]["literal_indicator_5"].AsString(), "5 indent spaces gone");

        Assert.Equal(
            root["strings"]["folded"].AsString(),
            "folded text continues here\nnew paragraph\n"
        );

        Assert.Equal(root["strings"]["folded_strip"].AsString(), "no trailing line break");
        Assert.Equal(
            root["strings"]["folded_keep"].AsString(),
            "keeps the trailing line breaks\n\n"
        );

        Assert.Equal(root["strings"]["non_ascii"].AsString(), "日本語 😀");

        Assert.Equal(root["sequences"][0].AsString(), "plain");
        Assert.Equal(root["sequences"][1].Count, 2);
        Assert.Equal(root["sequences"][1][0].AsString(), "nested");
        Assert.Equal(root["sequences"][1][1].AsString(), "sequence");
        Assert.Equal(root["sequences"][2].Count, 2);
        Assert.Equal(root["sequences"][2]["key"].AsString(), "in a sequence");
        Assert.Equal(root["sequences"][2]["other"].AsString(), "value");
        Assert.Equal(root["sequences"][3].Count, 2);
        Assert.Equal(root["sequences"][3][0].AsString(), "flow");
        Assert.Equal(root["sequences"][3][1].AsString(), "sequence");
        Assert.Equal(root["sequences"][4].Count, 1);
        Assert.Equal(root["sequences"][4]["flow"].AsString(), "map");
        Assert.Equal(root["sequences"][5].AsInt64(), 1);
        Assert.Equal(root["sequences"][6].AsTokenKind(), DTokenKind.Null);

        // A mapping below a bare dash must have the same value as a mapping on the dash's line.
        Assert.True(DValue.DeepEquals(root["sequences"][7], root["sequences"][2]));

        Assert.Equal(root["sequence_at_the_indent_of_its_key"].Count, 2);
        Assert.Equal(root["sequence_at_the_indent_of_its_key"][0].AsString(), "a");
        Assert.Equal(root["sequence_at_the_indent_of_its_key"][1].AsString(), "b");

        Assert.Equal(root["flow"]["sequence"].Count, 5);
        Assert.Equal(root["flow"]["sequence"][0].AsInt64(), 1);
        Assert.Equal(root["flow"]["sequence"][1].AsString(), "two");
        Assert.Equal(root["flow"]["sequence"][2].AsString(), "three");
        Assert.Equal(root["flow"]["sequence"][3].Count, 1);
        Assert.Equal(root["flow"]["sequence"][3][0].AsInt64(), 4);
        Assert.Equal(root["flow"]["sequence"][4].Count, 1);
        Assert.Equal(root["flow"]["sequence"][4]["five"].AsInt64(), 5);
        Assert.Equal(root["flow"]["empty_sequence"].Count, 0);
        Assert.Equal(root["flow"]["empty_map"].Count, 0);
        Assert.Equal(root["flow"]["map"]["a"].AsInt64(), 1);
        Assert.Equal(root["flow"]["map"]["b"].AsInt64(), 2);
        Assert.Equal(root["flow"]["map"]["c"].AsInt64(), 3);
        Assert.Equal(root["flow"]["map"]["d"].Count, 2);
        Assert.Equal(root["flow"]["map"]["d"][0].AsString(), "x");
        Assert.Equal(root["flow"]["map"]["d"][1].AsString(), "y");
        Assert.Equal(root["flow"]["multiline"].Count, 2);
        Assert.Equal(root["flow"]["multiline"][0].AsString(), "one");
        Assert.Equal(root["flow"]["multiline"][1].AsString(), "two");
        Assert.Equal(root["flow"]["trailing_comma"].Count, 2);
        Assert.Equal(root["flow"]["trailing_comma"][0].AsInt64(), 1);
        Assert.Equal(root["flow"]["trailing_comma"][1].AsInt64(), 2);
        Assert.Equal(root["flow"]["explicit_key"].Count, 1);
        Assert.Equal(root["flow"]["explicit_key"]["a"].AsString(), "b");

        // The keys are not all strings, so the entries are taken by position.
        var keys = root["keys"].AsElement().AsMap();

        Assert.Equal(keys[0].Key.AsInt64(), 1);
        Assert.Equal(keys[0].Value.AsString(), "integer key");
        Assert.Equal(keys[1].Key.AsBoolean(), true);
        Assert.Equal(keys[1].Value.AsString(), "boolean key");
        Assert.Equal(keys[2].Key.AsDouble(), 1.5);
        Assert.Equal(keys[2].Value.AsString(), "float key");
        Assert.Equal(keys[3].Key.AsTokenKind(), DTokenKind.Null);
        Assert.Equal(keys[3].Value.AsString(), "null key");

        Assert.Equal(
            keys[4].Key.AsDateTimeOffset(),
            new DateTimeOffset(2001, 12, 14, 0, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal(keys[4].Value.AsString(), "timestamp key");
        Assert.Equal(keys[5].Key.AsString(), "quoted key");
        Assert.Equal(keys[5].Value.AsString(), "value");
        Assert.Equal(keys[6].Key.AsString(), "single quoted key");
        Assert.Equal(keys[6].Value.AsString(), "value");
        Assert.Equal(keys[7].Key.AsString(), "explicit key");
        Assert.Equal(keys[7].Value.AsString(), "explicit value");
        Assert.Equal(keys[8].Key.AsString(), "block scalar key\n");
        Assert.Equal(keys[8].Value.AsString(), "value of the block scalar key");

        Assert.Equal(root["nesting"]["map_in_sequence_in_map"][0]["name"].AsString(), "first");
        Assert.Equal(root["nesting"]["map_in_sequence_in_map"][0]["tags"].Count, 2);
        Assert.Equal(root["nesting"]["map_in_sequence_in_map"][0]["tags"][0].AsString(), "a");
        Assert.Equal(root["nesting"]["map_in_sequence_in_map"][0]["tags"][1]["b"].Count, 3);
        Assert.Equal(root["nesting"]["map_in_sequence_in_map"][0]["tags"][1]["b"][0].AsInt64(), 1);
        Assert.Equal(root["nesting"]["map_in_sequence_in_map"][0]["tags"][1]["b"][1].AsInt64(), 2);
        Assert.Equal(
            root["nesting"]["map_in_sequence_in_map"][0]["tags"][1]["b"][2]["c"].AsString(),
            "d"
        );

        Assert.Equal(root["nesting"]["map_in_sequence_in_map"][0]["details"]["level"].AsInt64(), 1);

        Assert.Equal(
            root["nesting"]["map_in_sequence_in_map"][0]["details"]["deeper"]["level"].AsInt64(),
            2
        );

        Assert.Equal(
            root["nesting"]
                ["map_in_sequence_in_map"][0]["details"]["deeper"]["deepest"]["level"]
                .AsInt64(),
            3
        );

        Assert.Equal(
            root["nesting"]
                ["map_in_sequence_in_map"][0]["details"]["deeper"]["deepest"]["values"][1][1][1][0]
                .AsInt64(),
            4
        );

        Assert.Equal(root["nesting"]["map_in_sequence_in_map"][1]["name"].AsString(), "second");
        Assert.Equal(root["nesting"]["map_in_sequence_in_map"][1]["empty_sequence"].Count, 0);
        Assert.Equal(root["nesting"]["map_in_sequence_in_map"][1]["empty_map"].Count, 0);
        Assert.Equal(
            root["nesting"]["map_in_sequence_in_map"][1]["null_value"].AsTokenKind(),
            DTokenKind.Null
        );

        Assert.Equal(root["nesting"]["sequence_of_sequences"].Count, 2);
        Assert.Equal(root["nesting"]["sequence_of_sequences"][0][0].AsInt64(), 1);
        Assert.Equal(root["nesting"]["sequence_of_sequences"][0][1][0].AsInt64(), 2);
        Assert.Equal(root["nesting"]["sequence_of_sequences"][0][1][1][0].AsInt64(), 3);
        Assert.Equal(root["nesting"]["sequence_of_sequences"][0][1][1][1][0].AsInt64(), 4);
        Assert.Equal(root["nesting"]["sequence_of_sequences"][1][0][0][0].AsString(), "deep");
        Assert.Equal(root["nesting"]["flow_nested"].Count, 2);
        Assert.Equal(root["nesting"]["flow_nested"]["a"][0].AsInt64(), 1);
        Assert.Equal(root["nesting"]["flow_nested"]["a"][1]["b"][0].AsInt64(), 2);
        Assert.Equal(root["nesting"]["flow_nested"]["a"][1]["b"][1]["c"][0].AsInt64(), 3);
        Assert.Equal(root["nesting"]["flow_nested"]["a"][1]["b"][1]["c"][1]["d"][0].AsInt64(), 4);
        Assert.Equal(root["nesting"]["flow_nested"]["e"].AsString(), "f");
        Assert.Equal(
            root["nesting"]["block_scalars_nested"][0]["text"].AsString(),
            "literal in a map in a sequence\n"
        );

        Assert.Equal(
            root["nesting"]["block_scalars_nested"][0]["more"][0].AsString(),
            "folded in a sequence in a map\n"
        );

        Assert.Equal(
            root["nesting"]["block_scalars_nested"][0]["more"][1].AsString(),
            "plain after the folded scalar"
        );

        Assert.Equal(
            root["nesting"]["non_string_keys_nested"][0][0][0][0][0].AsString(),
            "float key in a map in a sequence"
        );
#pragma warning restore xUnit2000
    }
}

// Don't expose extension method broadly
file static class Extensions
{
    extension(DValue self)
    {
        public string? AsString() => (string?)self;

        public int AsInt32() => (int)self;

        public long AsInt64() => (long)self;

        public ulong AsUInt64() => (ulong)self;

        public double AsDouble() => (double)self;

        public bool AsBoolean() => (bool)self;

        public DateTimeOffset AsDateTimeOffset() => (DateTimeOffset)self;

        public DTokenKind AsTokenKind() => self.AsElement().Token.Kind;
    }
}