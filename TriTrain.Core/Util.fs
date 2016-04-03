﻿namespace TriTrain.Core

open System

[<AutoOpen>]
module Misc =
  let tap f x = f x; x

  let flip f x y = f y x

  type T3<'t> = 't * 't * 't
  type T7<'t> = 't * 't * 't * 't * 't * 't * 't

  type Rate = float

[<RequireQualifiedAccess>]
module T3 =
  let toList (x0, x1, x2) =
    [x0; x1; x2]

  let ofList =
    function
    | [x0; x1; x2] ->
        (x0, x1, x2) |> Some
    | _ -> None

[<AutoOpen>]
module T7 =
  let toList (x0, x1, x2, x3, x4, x5, x6) =
    [x0; x1; x2; x3; x4; x5; x6]

  let ofList =
    function
    | [x0; x1; x2; x3; x4; x5; x6] ->
        (x0, x1, x2, x3, x4, x5, x6) |> Some
    | _ -> None

[<RequireQualifiedAccess>]
module List =
  /// List.zip を行う。
  /// 長さが異なる場合は、短いほうに合わせて縮める。
  let zipShrink l r =
    let len = min (l |> List.length) (r |> List.length)
    let l = l |> List.take len
    let r = r |> List.take len
    in List.zip l r

  let tryMaxBy proj xs =
    xs
    |> List.fold (fun ma x ->
        let projX = proj x
        let ma' =
          match ma with
          | Some (max', projMax) when projMax >= projX -> ma
          | _ -> Some (x, projX)
        in ma'
        ) None
    |> Option.map fst

[<RequireQualifiedAccess>]
module Map =
  let singleton k v =
    Map.ofList [(k, v)]

  let append l r =
    r |> Map.fold (fun l k v -> l |> Map.add k v) l

  let keySet (m: Map<'k, 'v>): Set<'k> =
    m |> Map.toList |> List.map fst |> Set.ofList

  let valueSet (m: Map<'k, 'v>): Set<'v> =
    m |> Map.toList |> List.map snd |> Set.ofList

  let pullBack value m =
    m
    |> Map.toList
    |> List.choose (fun (k, v) ->
        if v = value then Some k else None
        )
    |> Set.ofList

[<RequireQualifiedAccess>]
module Random =
  let rng = Random()

  let roll (prob: float) =
    rng.NextDouble() < prob
    || prob >= 100.0

[<RequireQualifiedAccess>]
module Observable =
  open System.Diagnostics

  let indexed obs =
    obs
    |> Observable.scan
        (fun (opt, i) x -> (Some x, i + 1)) (None, -1)
    |> Observable.choose
        (fun (opt, i) -> opt |> Option.map (fun x -> (x, i)))

  let duplicateFirst obs =
    let obs' =
      obs
      |> indexed
      |> Observable.choose
          (fun (x, i) -> if i = 0 then Some x else None)
    in Observable.merge obs obs'

  type Source<'t>() =
    let protect f =
      let mutable ok = false
      try 
        f ()
        ok <- true
      finally
        Debug.Assert(ok, "IObserver method threw an exception.")

    let mutable key = 0
    let mutable subscriptions = (Map.empty: Map<int, IObserver<'t>>)

    let thisLock = new obj()

    let subscribe obs =
      let body () =
        key |> tap (fun k ->
          do key <- k + 1
          do subscriptions <- subscriptions |> Map.add k obs
          )
      in lock thisLock body

    let unsubscribe k =
      let body () =
        subscriptions <- subscriptions |> Map.remove k
      in
        lock thisLock body

    let next obs =
      subscriptions |> Map.iter (fun _ value ->
        protect (fun () -> value.OnNext(obs)))

    let completed () =
      subscriptions |> Map.iter (fun _ value ->
        protect (fun () -> value.OnCompleted()))

    let error err =
      subscriptions |> Map.iter (fun _ value ->
        protect (fun () -> value.OnError(err)))

    let obs = 
      { new IObservable<'T> with
          member this.Subscribe(obs) =
            let cancelKey = subscribe obs
            { new IDisposable with 
                member this.Dispose() = unsubscribe cancelKey
                }
          }

    let mutable finished = false

    member this.Next(obs) =
      Debug.Assert(not finished, "IObserver is already finished")
      next obs

    member this.Completed() =
      Debug.Assert(not finished, "IObserver is already finished")
      finished <- true
      completed()

    member this.Error(err) =
      Debug.Assert(not finished, "IObserver is already finished")
      finished <- true
      error err

    member this.AsObservable = obs
