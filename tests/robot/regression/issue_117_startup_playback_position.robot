*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-117    permanent    ui    startup

*** Test Cases ***
Startup Position Checkbox Has Clarified Label
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}    Main.SpeakExisting.Text    Start playback from latest user prompt

Startup Position Accessible Name Has Clarified Label
    [Template]    Ui Text Should Equal V1
    ${TEST_PROBE}    Main.SpeakExisting.Name    Start playback from latest user prompt

Startup Position Description Explains Startup Choice
    [Template]    Ui Text Should Contain V1
    ${TEST_PROBE}    Main.SpeakExisting.Description    startup playback

Startup Position Tooltip Explains On State
    [Template]    Ui Text Should Contain V1
    ${TEST_PROBE}
    ...    Main.SpeakExisting.Tooltip
    ...    On: start at the first speakable content at or after the latest user prompt.

Startup Position Tooltip Explains Off State
    [Template]    Ui Text Should Contain V1
    ${TEST_PROBE}
    ...    Main.SpeakExisting.Tooltip
    ...    Off: start at the current conversation end and wait for new content.

Startup Position Tooltip Is Split Across Two Lines
    ${result}=    Run Process    ${TEST_PROBE}    ui-text    Main.SpeakExisting.Tooltip    stdout=PIPE    stderr=PIPE
    Should Be Equal As Integers    ${result.rc}    0
    ${text}=    Evaluate    json.loads($result.stdout)    modules=json
    ${lf_count}=    Evaluate    $text.count(chr(10))
    ${cr_count}=    Evaluate    $text.count(chr(13))
    Should Be Equal As Integers    ${lf_count}    1
    Should Be Equal As Integers    ${cr_count}    0

MainForm Applies Startup Position Resource
    [Template]    File Should Contain Text V1
    ${EXECDIR}${/}AgentPanelSpeaker${/}MainForm.cs    UiText.Apply(_speakExistingCheckBox, "Main.SpeakExisting", _toolTip);
