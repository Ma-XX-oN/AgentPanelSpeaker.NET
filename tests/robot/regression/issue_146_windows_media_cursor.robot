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

Windows Media Metadata Diagnosis Preserves Every Generated Mark
    ${result}=    Run Process
    ...    ${TEST_PROBE}
    ...    static-method
    ...    AgentPanelSpeaker.Issue146WindowsMediaMetadataDiagnosticProbe
    ...    GetMetadataSnapshot
    ...    []
    ...    stdout=PIPE
    ...    stderr=PIPE
    Should Be Equal As Integers    ${result.rc}    0
    ...    msg=Issue #146 metadata diagnostic failed. Error: ${result.stderr}
    ${snapshot}=    Evaluate    json.loads($result.stdout)    modules=json
    FOR    ${case}    IN    @{snapshot}[Cases]
        Should Be Equal As Integers    ${case}[GeneratedMarkCount]    ${case}[ExpectedWordCount]
        ...    msg=Bookmark builder omitted a generated mark: ${case}
        Should Be Equal As Integers    ${case}[DistinctGeneratedMarkCount]    ${case}[ExpectedWordCount]
        ...    msg=Bookmark builder duplicated a generated mark: ${case}
    END

Missing Windows Media Bookmark Has Exact Ordered Native Recovery
    ${result}=    Run Process
    ...    ${TEST_PROBE}
    ...    static-method
    ...    AgentPanelSpeaker.Issue146WindowsMediaRecoveryDiagnosticProbe
    ...    GetRecoverySnapshot
    ...    []
    ...    stdout=PIPE
    ...    stderr=PIPE
    Should Be Equal As Integers    ${result.rc}    0
    ...    msg=Issue #146 exact recovery diagnostic failed. Error: ${result.stderr}
    ${snapshot}=    Evaluate    json.loads($result.stdout)    modules=json
    Should Be Equal As Integers    ${snapshot}[GeneratedMarkerCount]    ${snapshot}[ExpectedOwnerCount]
    ...    msg=Recovery evidence did not start from complete generated ownership: ${snapshot}
    ${missing_owner_count}=    Get Length    ${snapshot}[MissingOwners]
    Should Be Equal As Integers    ${missing_owner_count}    1
    ...    msg=Reproduced provider omission changed unexpectedly: ${snapshot}
    Should Be Equal As Integers    ${snapshot}[MissingOwners][0]    84
    ...    msg=Expected reproduced missing owner 84: ${snapshot}
    Should Be Equal As Integers    ${snapshot}[RecoverableOwnerCount]    1
    ...    msg=Missing owner was not uniquely recoverable: ${snapshot}
    Should Be Equal As Integers    ${snapshot}[Candidates][0][NativeDistinctRangeCount]    1
    ...    msg=Missing owner maps to an ambiguous native input range: ${snapshot}
    Should Be True    ${snapshot}[Candidates][0][NativeCueCount] > 0
    ...    msg=Missing owner has no provider-native word cues: ${snapshot}
    Should Be True    ${snapshot}[Candidates][0][StrictlyOrdered]
    ...    msg=Provider-native recovery time is not strictly between adjacent canonical bookmarks: ${snapshot}

Recovered Owner Reaches Speech Service And Real WebView As One Word
    ${result}=    Run Process
    ...    ${TEST_PROBE}
    ...    static-method
    ...    AgentPanelSpeaker.Issue146WindowsMediaApplicationPlaybackAcceptanceProbe
    ...    GetApplicationPlaybackSnapshot
    ...    []
    ...    stdout=PIPE
    ...    stderr=PIPE
    Should Be Equal As Integers    ${result.rc}    0
    ...    msg=Issue #146 application/WebView playback probe failed. Error: ${result.stderr}
    ${snapshot}=    Evaluate    json.loads($result.stdout)    modules=json
    Should Be Equal    ${snapshot}[HighlightMode]    Word
    ...    msg=Recovered playback was not projected as word-level highlighting: ${snapshot}
    Should Be Equal As Integers    ${snapshot}[ObservedTargetWordId]    ${snapshot}[ExpectedTargetWordId]
    ...    msg=SpeechService projected the wrong canonical WordId: ${snapshot}
    ${target_word_id_count}=    Get Length    ${snapshot}[ObservedTargetWordIds]
    Should Be Equal As Integers    ${target_word_id_count}    1
    ...    msg=Recovered owner did not project as exactly one canonical word: ${snapshot}
    Should Be Equal As Integers    ${snapshot}[ObservedTargetWordIds][0]    ${snapshot}[ExpectedTargetWordId]
    Should Be Equal    ${snapshot}[ObservedTargetText]    138
    ...    msg=Recovered owner did not retain the fixed source token: ${snapshot}
    Should Be Equal As Integers    ${snapshot}[ObservedTargetCharacterStart]    498
    Should Be Equal As Integers    ${snapshot}[ObservedTargetCharacterLength]    3
    Should Be True    ${snapshot}[ObservedCanonicalWordCount] > 1
    ...    msg=Application playback did not advance through multiple canonical words: ${snapshot}
    Should Not Be True    ${snapshot}[FragmentModeObserved]
    ...    msg=Reproduced application playback degraded to whole-fragment highlighting: ${snapshot}
    Should Be Equal    ${snapshot}[WebViewActiveWord]    138
    ...    msg=Real WebView did not visibly highlight recovered owner 84: ${snapshot}
    Should Be Equal As Integers    ${snapshot}[WebViewActiveWordCount]    1
    ...    msg=Real WebView highlighted more than the recovered canonical word: ${snapshot}

Windows Media Fails Closed When Exact Bookmark Ownership Is Unavailable
    ${result}=    Run Process
    ...    ${TEST_PROBE}
    ...    static-method
    ...    AgentPanelSpeaker.Issue146WindowsMediaApplicationPlaybackAcceptanceProbe
    ...    GetFailClosedSnapshot
    ...    []
    ...    stdout=PIPE
    ...    stderr=PIPE
    Should Be Equal As Integers    ${result.rc}    0
    ...    msg=Issue #146 fail-closed playback probe failed. Error: ${result.stderr}
    ${snapshot}=    Evaluate    json.loads($result.stdout)    modules=json
    Should Be Equal As Integers    ${snapshot}[ProviderBoundaryCount]    0
    ...    msg=Bookmarks-disabled provider path fabricated word boundaries: ${snapshot}
    Should Be Equal    ${snapshot}[ProviderDegradationReason]    windows_media_bookmarks_disabled
    ...    msg=Bookmarks-disabled provider path returned the wrong degradation reason: ${snapshot}
    Should Be Equal    ${snapshot}[HighlightMode]    Fragment
    ...    msg=Unavailable exact ownership did not degrade to the whole fragment: ${snapshot}
    Should Be Equal    ${snapshot}[WordId]    ${NONE}
    ...    msg=Fragment degradation retained a fabricated canonical WordId: ${snapshot}
    Should Be Equal As Integers    ${snapshot}[WordIdCount]    0
    ...    msg=Fragment degradation retained fabricated canonical WordIds: ${snapshot}
    Should Contain    ${snapshot}[Activity]    windows_media_bookmarks_disabled
    ...    msg=Fragment degradation did not expose the exact provider reason: ${snapshot}
