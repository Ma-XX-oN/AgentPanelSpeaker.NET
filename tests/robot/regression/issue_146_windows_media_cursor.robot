*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-146    permanent    speech    windows-media

*** Test Cases ***
Windows Media Retains Exact Word Cursor Across Reproduced Block
    ${result}=    Run Process
    ...    ${TEST_PROBE}
    ...    static-method
    ...    AgentPanelSpeaker.Issue146WindowsMediaCursorRegressionProbe
    ...    GetContractSnapshot
    ...    []
    ...    stdout=PIPE
    ...    stderr=PIPE
    Should Be Equal As Integers    ${result.rc}    0
    ...    msg=Issue #146 production Windows.Media cursor probe failed. Error: ${result.stderr}
    ${snapshot}=    Evaluate    json.loads($result.stdout)    modules=json
    Should Be True    ${snapshot}[Exact]
    ...    msg=Windows.Media returned non-exact cursor ownership: ${snapshot}
    Should Be Equal As Integers    ${snapshot}[ObservedWordCount]    ${snapshot}[ExpectedWordCount]
    ...    msg=Windows.Media did not retain every exact word owner: ${snapshot}
    Should Be Equal    ${snapshot}[DegradationReason]    ${EMPTY}
    ...    msg=Windows.Media degraded to whole-fragment highlighting: ${snapshot}
    Should Be True    ${snapshot}[BoundaryCount] > 1
    ...    msg=Windows.Media did not provide a moving word cursor: ${snapshot}
