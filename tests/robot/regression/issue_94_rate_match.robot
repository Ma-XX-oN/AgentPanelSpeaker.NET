*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-94    permanent    speech

*** Test Cases ***
Matched Positive Rate Uses Calibrated Desktop Scale
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}    AgentPanelSpeaker.SapiSpeechEngine    MapSystemSpeechRate    [6,true]    3

Matched Negative Rate Uses Calibrated Desktop Scale
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}    AgentPanelSpeaker.SapiSpeechEngine    MapSystemSpeechRate    [-6,true]    -3

Unmatched Positive Rate Preserves Native Desktop Rate
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}    AgentPanelSpeaker.SapiSpeechEngine    MapSystemSpeechRate    [6,false]    6

Unmatched Minimum Rate Preserves Native Desktop Range
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}    AgentPanelSpeaker.SapiSpeechEngine    MapSystemSpeechRate    [-10,false]    -10

Unmatched Maximum Rate Preserves Native Desktop Range
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}    AgentPanelSpeaker.SapiSpeechEngine    MapSystemSpeechRate    [10,false]    10

Rate Match Setting Defaults On And Persists Disabled
    [Template]    Setting Contract Should Match V1
    ${TEST_PROBE}
    ...    MatchDesktopAndWindowsMediaRates
    ...    false
    ...    Speech/MatchDesktopAndWindowsMediaRates
    ...    {"defaultValue":true,"roundTripValue":false,"changeKeyPresent":true,"selectedMergeValue":false}

Rate Match Checkbox Has Expected Label
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}
    ...    Main.MatchDesktopAndWindowsMediaRates.Text
    ...    Match Desktop and Windows Media rates

Rate Match Tooltip Explains Different Scales
    [Template]    Ui Text Should Contain V1
    ${TEST_PROBE}
    ...    Main.MatchDesktopAndWindowsMediaRates.Tooltip
    ...    different rate scales

Rate Match Tooltip Explains Native Range Tradeoff
    [Template]    Ui Text Should Contain V1
    ${TEST_PROBE}
    ...    Main.MatchDesktopAndWindowsMediaRates.Tooltip
    ...    native rate range

Rate Match Tooltip Is Split Across Two Lines
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}
    ...    Main.MatchDesktopAndWindowsMediaRates.Tooltip
    ...    Desktop/System.Speech and Windows Media use different rate scales.${\n}Enable this to make their speaking rates closer; this compresses the Desktop voice's available native rate range.

Application Tooltip Is Owner Drawn From Construction With Prompt Focus Delay
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}
    ...    AgentPanelSpeaker.AppToolTip
    ...    GetContractSnapshot
    ...    []
    ...    {"ownerDraw":true,"initialDelayMilliseconds":750,"reshowDelayMilliseconds":150,"autoPopDelayMilliseconds":7500,"showAlways":true,"keyboardFocusDelayMilliseconds":750}
