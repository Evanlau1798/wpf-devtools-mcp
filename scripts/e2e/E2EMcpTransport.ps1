Set-StrictMode -Version Latest

# One sequential client owns the writer and counter; record at the actual I/O
# boundary, not from later reconstructed request/response files.
function Write-E2EMcpTransportEvent {
    param(
        [Parameter(Mandatory)] [IO.StreamWriter] $Writer,
        [Parameter(Mandatory)] [ref] $Sequence,
        [Parameter(Mandatory)] [ValidateSet('sent', 'received')] [string] $Direction,
        [Parameter(Mandatory)] [System.Text.Json.JsonElement] $Message
    )

    if ($Sequence.Value -lt 0 -or $Sequence.Value -ge [int]::MaxValue) { throw 'Invalid transport sequence.' }
    if ($Message.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw 'RPC message must be an object.' }
    $record = [Collections.Generic.Dictionary[string, object]]::new()
    $record['sequence'] = [int]$Sequence.Value + 1
    $record['direction'] = $Direction
    $record['message'] = $Message.Clone()
    $json = [System.Text.Json.JsonSerializer]::Serialize($record, $record.GetType())
    $Writer.WriteLine($json)
    $Writer.Flush()
    $Sequence.Value = $record['sequence']
}
