*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-144    permanent    startup    history

*** Test Cases ***
In-Flight Paused History Is Reused By Monitoring
    ${result}=    Run Process
    ...    ${TEST_PROBE}
    ...    static-method
    ...    AgentPanelSpeaker.Issue144HistoryPreviewReuseRegressionProbe
    ...    GetContractSnapshot
    ...    []
    ...    stdout=PIPE
    ...    stderr=PIPE
    Should Be Equal As Integers    ${result.rc}    0
    ...    msg=Issue #144 production-path probe failed. Error: ${result.stderr}
    ${snapshot}=    Evaluate    json.loads($result.stdout)    modules=json
    Should Be True    ${snapshot}[PreviewWasInFlightAtPlay]
    ...    msg=Issue #144 fixture did not hold the paused preview in flight when Play started.
    Should Be Equal As Integers    ${snapshot}[HistoryLoadCount]    1
    ...    msg=Starting monitoring while preview is in flight built complete history more than once: ${snapshot}
    Should Be Equal As Integers    ${snapshot}[PreindexedReuseCount]    1
    ...    msg=Monitoring did not reuse the one prepared history snapshot: ${snapshot}
    Should Be True    ${snapshot}[MonitorReceivedPreparedSnapshot]
    ...    msg=Monitoring did not receive the exact prepared history snapshot instance: ${snapshot}
    Should Start With    ${snapshot}[LatestTurnUserText]    Issue 144 user source 0399.
    ...    msg=LatestTurn did not resolve from the final genuine User prompt: ${snapshot}
