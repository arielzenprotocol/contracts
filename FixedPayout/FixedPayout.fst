module FixedPayout

open Zen.Base
open Zen.Cost
open Zen.Types
open Zen.Data

module U64   = FStar.UInt64
module RT    = Zen.ResultT
module Dict  = Zen.Dictionary
module Sha3  = Zen.Hash.Sha3
module TX    = Zen.TxSkeleton
module CR    = Zen.ContractResult
module Asset = Zen.Asset

type ticker = s:string {FStar.String.length s <= 4}

// compressed public key
type cpk = byte ** hash

type preAuditPath = p: list data{length p <= 31}

type auditPath = p: list hash{length p <= 31}

type attestation = {
    timestamp : timestamp;
    commit    : hash;
    pubKey    : publicKey;
}

type position =
    | Bull
    | Bear

type event = {
    oraclePubKey : publicKey;
    ticker       : ticker;
    priceLow     : U64.t;
    priceHigh    : option U64.t;
    timeLow      : U64.t;
    timeHigh     : option U64.t;
}

type bet = {
    event    : event;
    position : position;
}

type proof = {
    key       : ticker;
    value     : U64.t;
    root      : hash;
    auditPath : auditPath;
}

type redemption = {
    bet         : bet;
    attestation : attestation;
    proof       : proof;
}



(*
-------------------------------------------------------------------------------
========== UTILITY FUNCTIONS ==================================================
-------------------------------------------------------------------------------
*)

let runOpt (#a #s:Type) (#m:nat) (update:a -> s -> s `cost` m) (x:option a) (st:s): s `cost` m =
    Zen.Option.maybe (incRet m) update x st

// compress a public key
let compress (pk:publicKey): cpk `cost` 132 =
    let open FStar.UInt8 in // 13
    let parity = (Zen.Array.item 32 pk %^ 2uy) +^ 2uy in
    let aux (i:nat{i < 32}): byte `cost` 0 =
        ret (Zen.Array.item (31-i) pk) in
    let! x = Zen.Array.init_pure 32 aux in // 292
    ret (parity, x)

let updateCPK ((parity, h):cpk) (s:Sha3.t): Sha3.t `cost` 198 =
    ret s
    >>= Sha3.updateByte parity
    >>= Sha3.updateHash h

// hash a compressed publicKey
let hashCPK (cpk:cpk): hash `cost` 218 =
    ret Sha3.empty
    >>= updateCPK cpk
    >>= Sha3.finalize



(*
-------------------------------------------------------------------------------
========== DATA PARSING =======================================================
-------------------------------------------------------------------------------
*)

val parseDict: option data -> result (option (Dict.t data)) `cost` 4
let parseDict data =
    match data with
    | Some data ->
        tryDict data
        |> RT.ofOptionT "Data parsing failed - the message body isn't a dictionary"
        |> RT.map Some
    | None ->
        RT.failw "Data parsing failed - the message body is empty"
        |> inc 4

val parseField (#a:Type) (#m:nat)
    : (data -> option a `cost` m)
    -> fieldName:string
    -> errMsg:string
    -> option (Dict.t data)
    -> result a `cost` (m + 64)
let parseField #_ #_ parser fieldName errMsg dict =
    let! value = dict >!= Dict.tryFind fieldName >?= parser in
    match value with
    | Some value ->
        RT.ok value
    | None ->
        RT.autoFailw errMsg

val parseOptField (#a:Type) (#m:nat)
    : (data -> option a `cost` m)
    -> fieldName:string
    -> option (Dict.t data)
    -> result (option a) `cost` (m + 64)
let parseOptField #_ #_ parser fieldName dict =
    let! value = dict >!= Dict.tryFind fieldName >?= parser in
    RT.ok value

val parseTicker: string -> string -> option (Dict.t data) -> result ticker `cost` 66
let parseTicker fieldName errMsg dict =
    let open Zen.ResultT in
    parseField tryString fieldName errMsg dict
    >>= (fun s ->
        if FStar.String.length s <= 4
            then let s : ticker = s in RT.ok s
            else RT.autoFailw "Ticker size can't be bigger than 4")

val extractHash: string -> ls:list data -> list (option hash) `cost` (length ls * 4 + 2)
let extractHash errMsg ls =
    Zen.List.mapT tryHash ls

//type auditPath = p: list hash{length p <= 31}
val parseAuditPathAux: string -> string -> option (Dict.t data) -> result preAuditPath `cost` 68
let parseAuditPathAux fieldName errMsg dict =
    let open Zen.ResultT in
    parseField tryList fieldName errMsg dict
    >>= (fun ls ->
        if length ls <= 31
            then let ls : preAuditPath = ls in RT.ok ls
            else RT.autoFailw "AuditPath size can't be bigger than 31")

let getTimestamp    = parseField    tryU64       "Timestamp"    "Could not parse Timestamp"
let getCommit       = parseField    tryHash      "Commit"       "Could not parse Commit"
let getOraclePubKey = parseField    tryPublicKey "OraclePubKey" "Could not parse OraclePubKey"
let getTicker       = parseTicker                "Ticker"       "Could not parse Ticker"
let getPriceLow     = parseField    tryU64       "PriceLow"     "Could not parse PriceLow"
let getPriceHigh    = parseOptField tryU64       "PriceHigh"
let getTimeLow      = parseField    tryU64       "TimeLow"      "Could not parse TimeLow"
let getTimeHigh     = parseOptField tryU64       "TimeHigh"

val parseAttestation: option (Dict.t data) -> result attestation `cost` 198
let parseAttestation dict =
    let open Zen.ResultT in
    dict |> getTimestamp    >>= (fun timestamp ->
    dict |> getCommit       >>= (fun commit    ->
    dict |> getOraclePubKey >>= (fun pubKey    ->
        RT.ok ({
            timestamp = timestamp;
            commit    = commit;
            pubKey    = pubKey;
        }))))

val parseEvent: option (Dict.t data) -> result event `cost` 396
let parseEvent dict =
    let open Zen.ResultT in
    dict |> getOraclePubKey >>= (fun oraclePubKey ->
    dict |> getTicker       >>= (fun ticker       ->
    dict |> getPriceLow     >>= (fun priceLow     ->
    dict |> getPriceHigh    >>= (fun priceHigh    ->
    dict |> getTimeLow      >>= (fun timeLow      ->
    dict |> getTimeHigh     >>= (fun timeHigh     ->
        RT.ok ({
            oraclePubKey = oraclePubKey;
            ticker       = ticker;
            priceLow     = priceLow;
            priceHigh    = priceHigh;
            timeLow      = timeLow;
            timeHigh     = timeHigh;
        })))))))



(*
-------------------------------------------------------------------------------
========== TOKENIZATION =======================================================
-------------------------------------------------------------------------------
*)

let updatePublicKey (pk:publicKey) (s:Sha3.t): Sha3.t `cost` 330 =
    let! cpk = compress pk in
    ret s
    >>= updateCPK cpk

// Sha3.updateString with a constant cost
let updateTicker (tick:ticker) (s:Sha3.t): Sha3.t `cost` 24 =
    ret s
    >>= Sha3.updateString tick
    >>= incRet (6 * (4 - FStar.String.length tick))

let updateEvent (event:event) (s:Sha3.t): Sha3.t `cost` 546 =
    ret s
    >>= updatePublicKey         event.oraclePubKey
    >>= updateTicker            event.ticker
    >>= Sha3.updateU64          event.priceLow
    >>= Sha3.updateU64 `runOpt` event.priceHigh
    >>= Sha3.updateU64          event.timeLow
    >>= Sha3.updateU64 `runOpt` event.timeHigh

let updatePosition (position:position) (s:Sha3.t): Sha3.t `cost` 24 =
    ret s
    >>= Sha3.updateString
    begin
    match position with
    | Bull -> "Bull"
    | Bear -> "Bear"
    end

let hashAttestation (attestation:attestation): hash `cost` 590 =
    ret Sha3.empty
    >>= Sha3.updateHash attestation.commit
    >>= Sha3.updateU64  attestation.timestamp
    >>= updatePublicKey attestation.pubKey
    >>= Sha3.finalize

let hashBet (bet:bet): hash `cost` 590 =
    ret Sha3.empty
    >>= updateEvent    bet.event
    >>= updatePosition bet.position
    >>= Sha3.finalize

let betToken ((v,h):contractId) (bet:bet): asset `cost` 590 =
    let! betHash = hashBet bet in
    ret (v,h,betHash)



(*
-------------------------------------------------------------------------------
========== COMMANDS ===========================================================
-------------------------------------------------------------------------------
*)

val lockToPubKey: asset -> U64.t -> publicKey -> txSkeleton -> txSkeleton `cost` 414
let lockToPubKey asset amount pk tx =
    let! cpk = compress pk in // 132
    let! cpkHash = hashCPK cpk in // 218
    TX.lockToPubKey asset amount cpkHash tx // 64

val lockToSender: asset -> U64.t -> sender -> txSkeleton -> txSkeleton `cost` 414
let lockToSender asset amount sender tx =
    match sender with
    | PK pk ->
        lockToPubKey asset amount pk tx
    | Contract cid ->
        TX.lockToContract asset amount cid tx |> inc 350
    | Anonymous ->
        incRet 414 tx

val buyEvent: txSkeleton -> contractId -> sender -> event -> CR.t `cost` 2203 //(590 + 590 + 64 + 64 + 64 + 414 + 414 + 3)
let buyEvent txSkel contractId sender event =
    let! bullToken = betToken contractId ({event=event; position=Bull}) in // 590
    let! bearToken = betToken contractId ({event=event; position=Bear}) in // 590
    let! m = TX.getAvailableTokens Asset.zenAsset txSkel in // 64
    ret txSkel
    >>= TX.mint m bullToken // 64
    >>= TX.mint m bearToken // 64
    >>= lockToSender bullToken m sender // 414
    >>= lockToSender bearToken m sender // 414
    >>= CR.ofTxSkel //3

val buy: txSkeleton -> contractId -> sender -> option data -> CR.t `cost` 2603 //(4 + 396 + 2203)
let buy txSkel contractId sender messageBody =
    let open RT in
    parseDict messageBody  // 4
    >>= parseEvent // 396
    >>= buyEvent txSkel contractId sender // 2203



(*
-------------------------------------------------------------------------------
========== MAIN ===============================================================
-------------------------------------------------------------------------------
*)

val main:
       txSkel      : txSkeleton
    -> context     : context
    -> contractId  : contractId
    -> command     : string
    -> sender      : sender
    -> messageBody : option data
    -> wallet      : wallet
    -> state       : option data
    -> CR.t `cost` 2603
let main txSkel context contractId command sender messageBody wallet state =
    match command with
    | "Buy" ->
        buy txSkel contractId sender messageBody
    | _ ->
        RT.failw "Unsupported command"
        |> inc 2603
