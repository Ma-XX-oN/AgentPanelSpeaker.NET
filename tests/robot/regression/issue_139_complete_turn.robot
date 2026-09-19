*** Settings ***
Resource      ../resources/TemplatesV1.resource
Force Tags    regression    issue-139    permanent    live-tail    claude

*** Test Cases ***
Complete Claude User And Assistant Turn Reaches Live Monitor
    ${result}=    Run Process
    ...    ${TEST_PROBE}
    ...    static-method
    ...    AgentPanelSpeaker.Issue139CompleteTurnRegressionProbe
    ...    GetContractSnapshot
    ...    []
    ...    stdout=PIPE
    ...    stderr=PIPE
    Should Be Equal As Integers    ${result.rc}    0
    ...    msg=Issue #139 complete-turn production-path probe failed. Error: ${result.stderr}
    ${snapshot}=    Evaluate    json.loads($result.stdout)    modules=json
    Should Be Equal As Integers    ${snapshot}[UserCount]    1
    ...    msg=The appended User prompt was not emitted exactly once: ${snapshot}
    Should Be Equal As Integers    ${snapshot}[AssistantCount]    1
    ...    msg=The appended Assistant response was not emitted exactly once: ${snapshot}
    Should Be True    ${snapshot}[UserBeforeAssistant]
    ...    msg=The appended User and Assistant records were not emitted in order: ${snapshot}
    Should Be True    ${snapshot}[UserStartsTurn]
    ...    msg=The appended User prompt did not retain User-turn semantics: ${snapshot}
    Should Not Be True    ${snapshot}[PrefixRepublished]
    ...    msg=Starting live monitoring republished the indexed prefix: ${snapshot}
    Should Be True    ${snapshot}[MonitorRunning]
    ...    msg=The monitor stopped during the complete appended turn: ${snapshot}
    Should Not Be True    ${snapshot}[Faulted]
    ...    msg=The monitor faulted during the complete appended turn: ${snapshot}
