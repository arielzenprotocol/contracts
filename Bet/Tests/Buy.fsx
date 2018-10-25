#load "Bet.fsx"
open Consensus
open Infrastructure
open Crypto
open Types
open Zen.Types.Data
open Zen.Data
open Zen.Types
open Bet
module Cost = Zen.Cost.Realized
module Tx = TxSkeleton

// all spends in the input
let rec inputSpends = function
    | input::inputs ->
        match input with
        | Tx.PointedOutput (_, output) ->
            output.spend :: inputSpends inputs
        | _ ->
            inputSpends inputs
    | [] -> []

// all spends in the output
let outputSpends = List.map (fun output -> output.spend)

// spends in the input locked to contract
let rec inputSpendsLockedToContract = function
    | input::inputs ->
        match input with
        | Tx.PointedOutput (_, output)
          when output.lock = Contract contractID ->
            output.spend :: inputSpendsLockedToContract inputs
        | _ ->
            inputSpendsLockedToContract inputs
    | [] -> []

// spends in the output locked to contract
let outputSpendsLockedToContract =
    List.filter (fun output -> output.lock = Contract contractID)
    >> List.map (fun output -> output.spend)

// spends in the input locked to returnAddressPK
let rec inputSpendsLockedToReturnAddressPK returnAddressPK = function
    | input::inputs ->
        match input with
        | Tx.PointedOutput (_, output)
          when output.lock = PK returnAddressPK ->
            output.spend :: inputSpendsLockedToReturnAddressPK returnAddressPK inputs
        | _ ->
            inputSpendsLockedToReturnAddressPK returnAddressPK inputs
    | [] -> []

// spends in the output locked to returnAddressPK
let outputSpendsLockedToReturnAddressPK returnAddressPK =
    List.filter (fun output -> output.lock = PK returnAddressPK)
    >> List.map (fun output -> output.spend)

// gets a list of minting inputs
let rec getMints = function
    | input::inputs ->
        match input with
        | Tx.Mint spend ->
            spend::getMints inputs
        | _ ->
            getMints inputs
    | [] -> []

// Total amount of ZP in a list of spends
let totalZP spends =
    spends |> List.filter (fun spend -> spend.asset = Asset.Zen)
           |> List.sumBy (fun spend -> spend.amount)

// Total amount of bull token in a list of spends
let totalBullToken spends =
    spends |> List.filter (fun spend -> spend.asset = bullToken)
           |> List.sumBy (fun spend -> spend.amount)

// Total amount of bear token in a list of spends
let totalBearToken spends =
    spends |> List.filter (fun spend -> spend.asset = bearToken)
           |> List.sumBy (fun spend -> spend.amount)


//////////////////////////////////////////////////////////////////////////
// Buy without returnAddress fails
//////////////////////////////////////////////////////////////////////////

match buy emptyTx emptyMessageBody with
| Error "Could not parse returnAddress from messageBody" ->
    printfn "OK: Buy without returnAddress fails"
| Ok _ -> failwith "Should not return ok without returnAddress"
| Error e -> failwithf "Failed with unexpected error: `%A`" e

//////////////////////////////////////////////////////////////////////////
// Buy with returnAddress and no ZP inputs fails
//////////////////////////////////////////////////////////////////////////

match buy emptyTx onlyReturnAddress with

| Error "Cannot buy with 0ZP in txSkeleton" ->
    printfn "OK: Buy with returnAddress and no ZP inputs fails"
| Ok _ -> failwith "Should not return ok with 0ZP in inputs"
| Error e -> failwithf "Failed with unexpected error: `%A`" e

//////////////////////////////////////////////////////////////////////////
// Buy with returnAddress and single 50ZP input
// Should lock 50ZP to contract
// Should mint 50 bull & bear tokens and spend them to returnAddress
//////////////////////////////////////////////////////////////////////////

let singleInputTx = mkTx [mkInput (Contract contractID) zp 50UL] []

match buy singleInputTx onlyReturnAddress with
| Ok ({pInputs=pInputs; outputs=outputs}, None, Main.NoChange) -> // expect no message or state update
    // inputs
    let inputMints = getMints pInputs
    let inputSpends = inputSpends pInputs
    let inputZP = totalZP inputSpends
    let inputSpendsLockedToContract = inputSpendsLockedToContract pInputs
    let inputSpendsLockedToReturnAddressPK =
        inputSpendsLockedToReturnAddressPK returnAddressPK pInputs
    let inputZPLockedToContract = totalZP inputSpendsLockedToContract
    let inputZPLockedToReturnAddressPK = totalZP inputSpendsLockedToReturnAddressPK
    let inputBullTokensLockedToContract = totalBullToken inputSpendsLockedToContract
    let inputBearTokensLockedToContract = totalBearToken inputSpendsLockedToContract
    let inputBullTokensLockedToReturnAddressPK = totalBullToken inputSpendsLockedToReturnAddressPK
    let inputBearTokensLockedToReturnAddressPK = totalBearToken inputSpendsLockedToReturnAddressPK
    let minted = getMints pInputs
    let bullTokensMinted = totalBullToken minted
    let bearTokensMinted = totalBearToken minted
    // Should be total of 50ZP in inputs
    if inputZP <> 50UL
        then failwithf "Expected 50ZP locked in input spends, but got: `%A`" pInputs
    // Should be total of 50ZP locked to contract in inputs
    if inputZPLockedToContract <> 50UL
        then failwithf "Expected 50ZP locked to contract in inputs, but got: `%A`" pInputs
    // Should be total of 50 Bull Tokens in input mints
    if bullTokensMinted <> 50UL
        then failwithf "Expected 50 Bull Tokens in input mints, but got: `%A`" pInputs
    // Should be total of 50 Bear Tokens in input mints
    if bearTokensMinted <> 50UL
        then failwithf "Expected 50 Bear Tokens in input mints, but got: `%A`" pInputs

    // outputs
    let outputSpends = outputSpends outputs
    let outputZP = totalZP outputSpends
    let outputBullTokens = totalBullToken outputSpends
    let outputBearTokens = totalBearToken outputSpends
    let outputSpendsLockedToContract = outputSpendsLockedToContract outputs
    let outputSpendsLockedToReturnAddressPK =
        outputSpendsLockedToReturnAddressPK returnAddressPK outputs
    let outputZPLockedToContract = totalZP outputSpendsLockedToContract
    let outputZPLockedToReturnAddressPK = totalZP outputSpendsLockedToReturnAddressPK
    let outputBullTokensLockedToContract = totalBullToken outputSpendsLockedToContract
    let outputBearTokensLockedToContract = totalBearToken outputSpendsLockedToContract
    let outputBullTokensLockedToReturnAddressPK = totalBullToken outputSpendsLockedToReturnAddressPK
    let outputBearTokensLockedToReturnAddressPK = totalBearToken outputSpendsLockedToReturnAddressPK
    // Should be total of 50ZP in outputs
    if outputZP <> 50UL
        then failwithf "Expected 50ZP locked in outputs, but got: `%A`" outputs
    // Should be total of 50ZP locked to contract in outputs
    if outputZPLockedToContract <> 50UL
        then failwithf "Expected 50ZP locked to contract in outputs, but got: `%A`" outputs
    // Should be total of 50 Bull Tokens in outputs
    if outputBullTokens <> 50UL
        then failwithf "Expected 50 Bull Tokens in outputs, but got: `%A`" outputs
    // Should be total of 50 Bull Tokens locked to returnAddress in outputs
    if outputBullTokensLockedToReturnAddressPK <> 50UL
        then failwithf "Expected 50 Bull Tokens locked to returnAddress in outputs, but got: `%A`" outputs
    // Should be total of 50 Bear Tokens in outputs
    if outputBearTokens <> 50UL
        then failwithf "Expected 50 Bear Tokens in outputs, but got: `%A`" outputs
    // Should be total of 50 Bear Tokens locked to returnAddress in outputs
    if outputBearTokensLockedToReturnAddressPK <> 50UL
        then failwithf "Expected 50 Bear Tokens locked to returnAddress in outputs, but got: `%A`" outputs

    // If you reach here, all is ok!
    printfn "OK: Buy with returnAddress and single 50ZP input"

| Ok (_, msg, Main.NoChange) ->
    failwithf "Expected no return message, but got: `%A`" msg
| Ok (_, _, stateUpdate) ->
    failwithf "Expected no state change, but got: `%A`" stateUpdate
| Error e ->
    failwithf "Failed with unexpected error: `%A`" e

//////////////////////////////////////////////////////////////////////////
// Buy with returnAddress and multiple ZP inputs totalling 150ZP
// Should lock 150ZP to contract
// Should mint 150 bull & bear tokens and spend them to returnAddress
//////////////////////////////////////////////////////////////////////////

let multiInputTx =
    let outpoint = {txHash=Hash.zero; index=0u}
    let spend x = {asset=Asset.Zen; amount=x}
    let output x = {lock=Contract contractID; spend=spend x}
    let pInput x = Tx.PointedOutput (outpoint, output x)
    let pInputs = [pInput 40UL; pInput 20UL; pInput 50UL; pInput 30UL; pInput 10UL]
    { Tx.pInputs=pInputs; Tx.outputs=[] }

match buy multiInputTx onlyReturnAddress with
| Ok ({pInputs=pInputs; outputs=outputs}, None, Main.NoChange) -> // expect no message or state update
    // inputs
    let inputMints = getMints pInputs
    let inputSpends = inputSpends pInputs
    let inputZP = totalZP inputSpends
    let inputSpendsLockedToContract = inputSpendsLockedToContract pInputs
    let inputSpendsLockedToReturnAddressPK =
        inputSpendsLockedToReturnAddressPK returnAddressPK pInputs
    let inputZPLockedToContract = totalZP inputSpendsLockedToContract
    let inputZPLockedToReturnAddressPK = totalZP inputSpendsLockedToReturnAddressPK
    let inputBullTokensLockedToContract = totalBullToken inputSpendsLockedToContract
    let inputBearTokensLockedToContract = totalBearToken inputSpendsLockedToContract
    let inputBullTokensLockedToReturnAddressPK = totalBullToken inputSpendsLockedToReturnAddressPK
    let inputBearTokensLockedToReturnAddressPK = totalBearToken inputSpendsLockedToReturnAddressPK
    let minted = getMints pInputs
    let bullTokensMinted = totalBullToken minted
    let bearTokensMinted = totalBearToken minted
    // Should be total of 150ZP in inputs
    if inputZP <> 150UL
        then failwithf "Expected 150ZP locked in input spends, but got: `%A`" pInputs
    // Should be total of 150ZP locked to contract in inputs
    if inputZPLockedToContract <> 150UL
        then failwithf "Expected 150ZP locked to contract in inputs, but got: `%A`" pInputs
    // Should be total of 150 Bull Tokens in input mints
    if bullTokensMinted <> 150UL
        then failwithf "Expected 150 Bull Tokens in input mints, but got: `%A`" pInputs
    // Should be total of 150 Bear Tokens in input mints
    if bearTokensMinted <> 150UL
        then failwithf "Expected 150 Bear Tokens in input mints, but got: `%A`" pInputs

    // outputs
    let outputSpends = outputSpends outputs
    let outputZP = totalZP outputSpends
    let outputBullTokens = totalBullToken outputSpends
    let outputBearTokens = totalBearToken outputSpends
    let outputSpendsLockedToContract = outputSpendsLockedToContract outputs
    let outputSpendsLockedToReturnAddressPK =
        outputSpendsLockedToReturnAddressPK returnAddressPK outputs
    let outputZPLockedToContract = totalZP outputSpendsLockedToContract
    let outputZPLockedToReturnAddressPK = totalZP outputSpendsLockedToReturnAddressPK
    let outputBullTokensLockedToContract = totalBullToken outputSpendsLockedToContract
    let outputBearTokensLockedToContract = totalBearToken outputSpendsLockedToContract
    let outputBullTokensLockedToReturnAddressPK = totalBullToken outputSpendsLockedToReturnAddressPK
    let outputBearTokensLockedToReturnAddressPK = totalBearToken outputSpendsLockedToReturnAddressPK
    // Should be total of 150ZP in outputs
    if outputZP <> 150UL
        then failwithf "Expected 150ZP locked in outputs, but got: `%A`" outputs
    // Should be total of 150ZP locked to contract in outputs
    if outputZPLockedToContract <> 150UL
        then failwithf "Expected 150ZP locked to contract in outputs, but got: `%A`" outputs
    // Should be total of 150 Bull Tokens in outputs
    if outputBullTokens <> 150UL
        then failwithf "Expected 150 Bull Tokens in outputs, but got: `%A`" outputs
    // Should be total of 150 Bull Tokens locked to returnAddress in outputs
    if outputBullTokensLockedToReturnAddressPK <> 150UL
        then failwithf "Expected 150 Bull Tokens locked to returnAddress in outputs, but got: `%A`" outputs
    // Should be total of 150 Bear Tokens in outputs
    if outputBearTokens <> 150UL
        then failwithf "Expected 150 Bear Tokens in outputs, but got: `%A`" outputs
    // Should be total of 150 Bear Tokens locked to returnAddress in outputs
    if outputBearTokensLockedToReturnAddressPK <> 150UL
        then failwithf "Expected 150 Bear Tokens locked to returnAddress in outputs, but got: `%A`" outputs

    // If you reach here, all is ok!
    printfn "OK: Buy with returnAddress and multiple ZP inputs totalling 150ZP"

| Ok (_, msg, Main.NoChange) ->
    failwithf "Expected no return message, but got: `%A`" msg
| Ok (_, _, stateUpdate) ->
    failwithf "Expected no state change, but got: `%A`" stateUpdate
| Error e ->
    failwithf "Failed with unexpected error: `%A`" e
