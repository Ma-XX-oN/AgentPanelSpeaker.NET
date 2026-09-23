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
    ${result}=    Run Process    ${TEST_PROBE}    ui-text    Main.MatchDesktopAndWindowsMediaRates.Tooltip    stdout=PIPE    stderr=PIPE
    Should Be Equal As Integers    ${result.rc}    0
    ${text}=    Evaluate    json.loads($result.stdout)    modules=json
    ${lf_count}=    Evaluate    $text.count(chr(10))
    ${cr_count}=    Evaluate    $text.count(chr(13))
    Should Be Equal As Integers    ${lf_count}    1
    Should Be Equal As Integers    ${cr_count}    0

Application Tooltip Uses Explicit Pointer And Focus Scheduling
    [Template]    Static Method Should Return V1
    ${TEST_PROBE}
    ...    AgentPanelSpeaker.AppToolTip
    ...    GetContractSnapshot
    ...    []
    ...    {"ownerDraw":true,"autoPopDelayMilliseconds":7500,"showAlways":true,"presentationDelayMilliseconds":750,"pointerHoverDelayMilliseconds":750,"keyboardFocusDelayMilliseconds":750,"nativeAutomaticHoverSuppressed":true,"pointerEnterSchedulesPresentation":true,"pointerLeaveCancelsWithoutFocus":true,"keyboardEnterSchedulesPresentation":true,"pointerLeavePreservesFocus":true,"focusLeavePreservesPointer":true}
