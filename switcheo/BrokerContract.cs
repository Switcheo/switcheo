using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace switcheo
{
    public class BrokerContract : SmartContract
    {
        public delegate object NEP5Contract(string method, object[] args);

        // Events
        [DisplayName("created")]
        public static event Action<byte[], byte[], byte[], BigInteger, byte[], BigInteger> EmitCreated; // (address, offerHash, offerAssetID, offerAmount, wantAssetID, wantAmount)

        [DisplayName("filled")]
        public static event Action<byte[], byte[], BigInteger, byte[], BigInteger, byte[], BigInteger, BigInteger> EmitFilled; // (address, offerHash, fillAmount, offerAssetID, offerAmount, wantAssetID, wantAmount, amountToTake)

        [DisplayName("failed")]
        public static event Action<byte[], byte[], BigInteger, byte[], BigInteger, byte[]> EmitFailed; // (address, offerHash, amountToTake, takerFeeAsssetID, takerFee, reason)

        [DisplayName("cancelAnnounced")]
        public static event Action<byte[], byte[]> EmitCancelAnnounced; // (address, offerHash)

        [DisplayName("cancelled")]
        public static event Action<byte[], byte[]> EmitCancelled; // (address, offerHash)

        [DisplayName("transferred")]
        public static event Action<byte[], byte[], BigInteger, byte[]> EmitTransferred; // (address, assetID, amount, reason)

        [DisplayName("deposited")]
        public static event Action<byte[], byte[], BigInteger> EmitDeposited; // (address, assetID, amount)

        [DisplayName("withdrawAnnounced")]
        public static event Action<byte[], byte[], BigInteger> EmitWithdrawAnnounced; // (address, assetID, amount)

        [DisplayName("withdrawing")]
        public static event Action<byte[], byte[], BigInteger> EmitWithdrawing; // (address, assetID, amount)

        [DisplayName("withdrawn")]
        public static event Action<byte[], byte[], BigInteger, byte[]> EmitWithdrawn; // (address, assetID, amount, utxoUsed)

        // Emitted when a user approved a spender not of this contract
        [DisplayName("spenderApproved")]
        public static event Action<byte[], byte[]> EmitSpenderApproved; // (user, spender)

        // Emitted when a user rescinds approval for a spender not of this contract
        [DisplayName("spenderRescinded")]
        public static event Action<byte[], byte[]> EmitSpenderRescinded; // (user, spender)

        // Emitted when the contract owner whitelists a spender not of this contract for users to approve
        [DisplayName("spenderAdded")]
        public static event Action<byte[]> EmitSpenderAdded; // (spender)

        // Emitted when the contract owner removes a whitelisted spender so that users can rescind approval
        [DisplayName("spenderRemoved")]
        public static event Action<byte[]> EmitSpenderRemoved; // (spender)

        [DisplayName("burnt")]
        public static event Action<byte[], byte[], BigInteger> EmitBurnt; // (address, assetID, amount)

        [DisplayName("tradingFrozen")]
        public static event Action EmitTradingFrozen;

        [DisplayName("tradingResumed")]
        public static event Action EmitTradingResumed;

        [DisplayName("addedToWhitelist")]
        public static event Action<byte[], int> EmitAddedToWhitelist; // (scriptHash, whitelistEnum)

        [DisplayName("removedFromWhitelist")]
        public static event Action<byte[], int> EmitRemovedFromWhitelist; // (scriptHash, whitelistEnum)

        [DisplayName("whitelistSealed")]
        public static event Action<int> EmitWhitelistSealed; // (whitelistEnum)

        [DisplayName("feeAddressSet")]
        public static event Action<byte[]> EmitFeeAddressSet; // (address)

        [DisplayName("coordinatorSet")]
        public static event Action<byte[]> EmitCoordinatorSet; // (address)

        [DisplayName("withdrawCoordinatorSet")]
        public static event Action<byte[]> EmitWithdrawCoordinatorSet; // (address)

        [DisplayName("announceDelaySet")]
        public static event Action<BigInteger> EmitAnnounceDelaySet; // (delay)

        [DisplayName("swapCreated")]
        public static event Action<byte[], byte[], byte[], BigInteger, byte[], BigInteger, byte[], BigInteger> EmitSwapCreated; // (makerAddress, takerAddress, assetID, amount, hashedSecret, expiryTime, feeAssetID, feeAmount)

        [DisplayName("swapExecuted")]
        public static event Action<byte[]> EmitSwapExecuted; // (hashedSecret)

        [DisplayName("swapCancelled")]
        public static event Action<byte[], BigInteger> EmitSwapCancelled; // (hashedSecret, cancelFeeAmount)

        [DisplayName("initialized")]
        public static event Action Initialized;

        // Broker Settings & Hardcaps
        private static readonly byte[] Owner = "AZThHNqfUGV9TTPsz2i7VT69iUwfySXGW9".ToScriptHash();
        private static readonly ulong maxAnnounceDelay = 60 * 60 * 24 * 7; // 7 days

        // Transaction Types
        private static readonly byte ClaimTransactionType = 0x02;
        private static readonly byte ContractTransactionType = 0x80;
        private static readonly byte InvocationTransactionType = 0xd1;

        // Contract States
        private static readonly byte[] Pending = { };         // only can initialize
        private static readonly byte[] Active = { 0x01 };     // all operations active
        private static readonly byte[] Inactive = { 0x02 };   // trading halted - only can do cancel, withdrawal & owner actions

        // Withdrawal Flags
        private static readonly byte[] Mark = { 0x50 };
        private static readonly byte[] Withdraw = { 0x51 };
        private static readonly byte[] OpCode_TailCall = { 0x69 };
        private static readonly byte TAUsage_WithdrawalStage = 0xa1;
        private static readonly byte TAUsage_NEP5AssetID = 0xa2;
        private static readonly byte TAUsage_SystemAssetID = 0xa3;
        private static readonly byte TAUsage_WithdrawalAddress = 0xa4;
        private static readonly byte TAUsage_WithdrawalAmount = 0xa5;

        // Byte Constants
        private static readonly byte[] Empty = { };
        private static readonly byte[] Zero = { 0x00 };
        private static readonly byte[] Zeroes = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; // 32 bytes of zeroes
        private static readonly byte[] NeoAssetID = { 155, 124, 255, 218, 166, 116, 190, 174, 15, 147, 14, 190, 96, 133, 175, 144, 147, 229, 254, 86, 179, 74, 92, 34, 12, 205, 207, 110, 252, 51, 111, 197 };
        private static readonly byte[] GasAssetID = { 231, 45, 40, 105, 121, 238, 108, 177, 183, 230, 93, 253, 223, 178, 227, 132, 16, 11, 141, 20, 142, 119, 88, 222, 66, 228, 22, 139, 113, 121, 44, 96 };
        private static readonly byte[] MctAssetID = { 63, 188, 96, 124, 18, 194, 135, 54, 52, 50, 36, 164, 180, 216, 245, 19, 165, 194, 124, 168 };
        // private static readonly byte[] MctAssetID = { 161, 47, 30, 104, 24, 178, 176, 62, 14, 249, 6, 237, 39, 46, 18, 9, 144, 116, 169, 38 };
        private static readonly byte[] WithdrawArgs = { 0x00, 0xc1, 0x08, 0x77, 0x69, 0x74, 0x68, 0x64, 0x72, 0x61, 0x77 }; // PUSH0, PACK, PUSHBYTES8, "withdraw" as bytes

        //* Reason Code for balance changes *//
        // Deposits
        private static readonly byte[] ReasonDeposit = { 0x01 }; // Balance increased due to deposit
        // Making an offer
        private static readonly byte[] ReasonMakerGive = { 0x02 }; // Balance reduced due to tokens locked when a maker makes an offer
        private static readonly byte[] ReasonMakerFeeGive = { 0x10 }; // Balance reduced due to tokens locked for maker fees
        private static readonly byte[] ReasonTakerGive = { 0x03 }; // Balance reduced due to taker filling maker's wanted asset
        private static readonly byte[] ReasonTakerFeeGive = { 0x04 }; // Balance reduced due to taker fees
        private static readonly byte[] ReasonTakerReceive = { 0x05 }; // Balance increased due to taker receiving his cut in the trade
        private static readonly byte[] ReasonMakerReceive = { 0x06 }; // Balance increased due to maker receiving his cut in the trade
        private static readonly byte[] ReasonTakerFeeReceive = { 0x07 }; // Balance increased on fee address due to contract receiving taker fee
        private static readonly byte[] ReasonMakerFeeReceive = { 0x11 }; // Balance increased on fee address due to contract receiving maker fee
        private static readonly byte[] ReasonMakerFeeRefund = { 0x1A }; // Balance increased on maker address due to refunding remaining locked fees when cancelling an offer
        private static readonly byte[] ReasonCancel = { 0x08 }; // Balance increased due to cancelling offer
        private static readonly byte[] ReasonWithdrawal = { 0x09 }; // Balance reduced due to withdrawal from contract
        private static readonly byte[] ReasonSweeperGive = { 0x16 }; // Balance reduced due to sweeper sweeping dust token from contract balance
        private static readonly byte[] ReasonSweepCounterpartyReceive = { 0x17 }; // Balance increased due to counterparty receiving token from sweeper
        private static readonly byte[] ReasonSweepCounterpartyGive = { 0x18 }; // Balance reduced due to counterparty giving token to sweeper
        private static readonly byte[] ReasonSweeperReceive = { 0x19 }; // Balance increased due to sweeper receiving token from counterparty

        // Reason Code for fill failures
        private static readonly byte[] ReasonOfferNotExist = { 0x21 }; // Empty Offer when trying to fill
        private static readonly byte[] ReasonTakingLessThanOne = { 0x22 }; // Taking less than 1 token when trying to fill
        private static readonly byte[] ReasonFillerSameAsMaker = { 0x23 }; // Filler same as maker
        private static readonly byte[] ReasonTakingMoreThanAvailable = { 0x24 }; // Taking more than available in the offer at the moment
        private static readonly byte[] ReasonFillingLessThanOne = { 0x25 }; // Filling less than 1 token when trying to fill
        private static readonly byte[] ReasonNotEnoughBalanceOnFiller = { 0x26 }; // Not enough balance to give (wantAssetID) for what you want to take (offerAssetID)
        private static readonly byte[] ReasonNotEnoughBalanceOnNativeToken = { 0x27 }; // Not enough balance in native tokens to use
        private static readonly byte[] ReasonNotEnoughBalanceOnRemainingMakerFee = { 0x28 }; // Not enough balance in offer's remaining maker fee deductible

        // Atomic Swaps Balance Change Reason Codes

        // Creating a swap
        private static readonly byte[] ReasonSwapMakerGive = { 0x30 };
        private static readonly byte[] ReasonSwapMakerFeeGive = { 0x32 };

        // Executing a swap
        private static readonly byte[] ReasonSwapTakerReceive = { 0x35 };
        private static readonly byte[] ReasonSwapFeeReceive = { 0x37 };

        // Cancelling a swap
        private static readonly byte[] ReasonSwapCancelMakerReceive = { 0x38 };
        private static readonly byte[] ReasonSwapCancelFeeReceive = { 0x3B };
        private static readonly byte[] ReasonSwapCancelFeeRefundReceive = { 0x3D };

        private struct Offer
        {
            public byte[] MakerAddress;
            public byte[] OfferAssetID;
            public BigInteger OfferAmount;
            public byte[] WantAssetID;
            public BigInteger WantAmount;
            public BigInteger AvailableAmount;
            public byte[] MakerFeeAssetID;
            public BigInteger MakerFeeAvailableAmount;
            public byte[] Nonce;
        }

        private static Map<byte[], BalanceChange[]> NewBalanceChanges()
        {
            return new Map<byte[], BalanceChange[]>();
        }

        private static void ExecuteBalanceChanges(Map<byte[], BalanceChange[]> balanceChanges)
        {
            // Loop each address that needs to be updated
            foreach(byte[] address in balanceChanges.Keys)
            {
                // Get current blockchain state
                var key = BalanceKey(address);
                var serializedBalances = Storage.Get(Context(), key);
                Map<byte[], BigInteger> balances;
                if (serializedBalances.Length > 1)
                {
                    balances = (Map<byte[], BigInteger>)serializedBalances.Deserialize();
                }
                else // Else create it
                {
                    balances = new Map<byte[], BigInteger>();
                }

                BalanceChange[] addressBalanceChanges = balanceChanges[address];
                foreach(BalanceChange change in addressBalanceChanges)
                {
                    // Init empty balance if none is found
                    if (!balances.HasKey(change.AssetID)) balances[change.AssetID] = 0;
                    // Add positive or negative balance
                    balances[change.AssetID] += change.Amount;
                    if (balances[change.AssetID] < 0) throw new Exception("Balance went to negative!");
                    EmitTransferred(address, change.AssetID, change.Amount, change.ReasonCode);
                }
                // Save to blockchain after all changes are updated for this address
                Storage.Put(Context(), key, balances.Serialize());
            }
        }

        private static Offer NewOffer(
            byte[] makerAddress,
            byte[] offerAssetID, byte[] offerAmount,
            byte[] wantAssetID, byte[] wantAmount,
            byte[] makerFeeAssetID, byte[] makerFeeAvailableAmount,
            byte[] nonce
        )
        {
            return new Offer
            {
                MakerAddress = makerAddress.Take(20),
                OfferAssetID = offerAssetID,
                OfferAmount = offerAmount.AsBigInteger(),
                WantAssetID = wantAssetID,
                WantAmount = wantAmount.AsBigInteger(),
                AvailableAmount = offerAmount.AsBigInteger(),
                MakerFeeAssetID = makerFeeAssetID,
                MakerFeeAvailableAmount = makerFeeAvailableAmount.AsBigInteger(),
                Nonce = nonce,
            };
        }

        private struct Swap
        {
            public byte[] MakerAddress;
            public byte[] TakerAddress;
            public byte[] AssetID;
            public BigInteger Amount;
            public BigInteger ExpiresAt;
            public byte[] FeeAssetID;
            public BigInteger FeeAmount;
            public bool BurnTokens;
            public bool Active;
        }

        private struct AnnouncementInfo
        {
            public BigInteger TimeStamp;
            public BigInteger Amount;
        }

        /// <summary>
        ///   This is the Switcheo smart contract entrypoint.
        ///
        ///   Parameter List: 0710
        ///   Return List: 05
        /// </summary>
        /// <param name="operation">
        ///   The method to be invoked.
        /// </param>
        /// <param name="args">
        ///   Input parameters for the delegated method.
        /// </param>
        public static object Main(string operation, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                if (GetState() == Pending) return false;

                var currentTxn = (Transaction)ExecutionEngine.ScriptContainer;
                var withdrawalStage = GetWithdrawalStage(currentTxn);
                var withdrawingAddr = GetWithdrawalAddress(currentTxn);
                var assetID = GetWithdrawalAsset(currentTxn);
                var isWithdrawingNEP5 = IsToken(assetID);
                var inputs = currentTxn.GetInputs();
                var outputs = currentTxn.GetOutputs();
                var references = currentTxn.GetReferences();

                // Handle UTXO consolidations
                if (currentTxn.Type == ContractTransactionType)
                {
                    // Check that inputs are not already reserved (We must not re-use a UTXO that is already reserved)
                    if (!IsAllInputsUnreserved(inputs)) return false;

                    // Check that no assets are transferred out from contract
                    if (!IsFullTransferToSelf(references, outputs, GasAssetID)) return false;
                    if (!IsFullTransferToSelf(references, outputs, NeoAssetID)) return false;

                    // Check that consolidation is signed off by coordinator to prevent DOS
                    if (!Runtime.CheckWitness(GetCoordinatorAddress())) return false;

                    return true;
                }

                // Handle GAS claims
                if (currentTxn.Type == ClaimTransactionType)
                {
                    // Ensure nothing is sent out from this contract
                    foreach (var i in references)
                    {
                        if (i.ScriptHash == ExecutionEngine.ExecutingScriptHash) return false;
                    }
                    // Only allow claims to be sent to fee address for manual dispersal
                    if (outputs.Length > 1 || outputs[0].ScriptHash != GetFeeAddress()) return false;
                    return true;
                }

                // Must be an InvocationTransaction if not a Contract or Claim
                if (currentTxn.Type != InvocationTransactionType) return false;

                // Check that Application trigger will be tail called with the correct params
                if (((InvocationTransaction)currentTxn).Script != WithdrawArgs.Concat(OpCode_TailCall).Concat(ExecutionEngine.ExecutingScriptHash)) return false;

                if (withdrawalStage == Mark)
                {
                    // Check that this is a valid SystemAsset withdrawal
                    ulong amount = (ulong)outputs[0].Value;
                    if (amount == 0 || outputs.Length > 2 || isWithdrawingNEP5) return false;

                    // Check that inputs are not already reserved (We must not re-use a UTXO that is already reserved)
                    if (!IsAllInputsUnreserved(inputs)) return false;

                    // Check that nothing is burnt from the contract
                    ulong totalIn = 0;
                    foreach (var i in references)
                    {
                        if (i.ScriptHash == ExecutionEngine.ExecutingScriptHash)
                        {
                            if (i.AssetId != assetID) return false;
                            totalIn += (ulong)i.Value;
                        }
                    }
                    ulong totalOut = SumOutputAmounts(outputs, ExecutionEngine.ExecutingScriptHash, assetID);
                    if (totalIn != totalOut) return false;

                    // Check that only required inputs are used (if deleting the last input causes the totalIn to still be > amount that means the last input was useless and is wasting UTXOs)
                    ulong lastInput = (ulong)references[references.Length - 1].Value;
                    if (totalIn - lastInput > amount) return false;

                    // Check that the withdrawing address has sufficient contract balance
                    if (!VerifyWithdrawalValid(withdrawingAddr, assetID, amount)) return false;

                    // Check that the transaction is signed by the withdraw coordinator, or...
                    if (!Runtime.CheckWitness(GetWithdrawCoordinatorAddress()))
                    {
                        // If not signed by withdraw coordinator it must be pre-announced + signed by the user
                        if (!Runtime.CheckWitness(withdrawingAddr)) return false;
                        if (!IsWithdrawalAnnounced(withdrawingAddr, assetID, amount)) return false;
                    }

                    return true;
                }

                if (withdrawalStage == Withdraw)
                {
                    if (isWithdrawingNEP5)
                    {
                        // Check that NEP5 withdrawals don't use contract assets
                        foreach (var i in references)
                        {
                            if (i.ScriptHash == ExecutionEngine.ExecutingScriptHash) return false;
                        }

                        // Check for whitelist if we are doing old style NEP-5 transfers;
                        // New style NEP-5 transfers SHOULD NOT require contract verification / witness
                        if (!IsWhitelistedOldNEP5(assetID)) return false;

                        // Check that the withdrawing address has sufficient contract balance
                        if (!VerifyWithdrawalValid(withdrawingAddr, assetID, GetWithdrawalAmount(currentTxn))) return false;
                    }
                    else
                    {
                        var reservedUTXO = inputs[0];

                        // Check that UTXO is reserved (i.e. not a change output, and is allocated to this withdrawing address)
                        if (reservedUTXO.PrevIndex != 0 || Storage.Get(Context(), WithdrawalAddressKey(reservedUTXO.PrevHash)) != withdrawingAddr) return false;

                        // Check that no other UTXOs comes from the contract
                        for (var i = 1; i < references.Length; i++)
                        {
                            if (references[i].ScriptHash == ExecutionEngine.ExecutingScriptHash) return false;
                        }

                        // Check that the withdrawal amount goes to the correct destination
                        ulong totalOut = SumOutputAmounts(outputs, withdrawingAddr, assetID);
                        if (totalOut != (ulong)references[0].Value) return false;
                    }

                    return true;
                }

                return false;
            }

            if (Runtime.Trigger == TriggerType.Application)
            {
                // == Init ==
                if (operation == "initialize")
                {
                    if (!Runtime.CheckWitness(Owner))
                    {
                        Runtime.Log("Owner signature verification failed!");
                        return false;
                    }
                    if (args.Length != 3) return false;
                    return Initialize((byte[])args[0], (byte[])args[1], (byte[])args[2]);
                }

                // == Getters ==
                if (operation == "getState") return GetState();
                if (operation == "getOffer") return GetOffer((byte[])args[0]);
                if (operation == "getBalance") return GetBalance((byte[])args[0], (byte[])args[1]);
                if (operation == "getIsWhitelisted") return GetIsWhitelisted((byte[])args[0], (int)args[1]);  // (assetID, whitelistEnum)
                if (operation == "getFeeAddress") return GetFeeAddress();
                if (operation == "getCoordinatorAddress") return GetCoordinatorAddress();
                if (operation == "getWithdrawCoordinatorAddress") return GetWithdrawCoordinatorAddress();
                if (operation == "getAnnounceDelay") return GetAnnounceDelay();
                if (operation == "getAnnouncedWithdraw") return GetAnnouncedWithdrawal((byte[])args[0], (byte[])args[1]);  // (originator, assetID)
                if (operation == "getAnnouncedCancel") return GetAnnouncedCancel((byte[])args[0]);  // (offerHash)

                // == Execute ==
                if (operation == "deposit") // (originator, assetID, amount)
                {
                    if (args.Length != 3) return false;
                    return Deposit((byte[])args[0], (byte[])args[1], (BigInteger)args[2]);
                }
                if (operation == "depositFrom") // (originator, assetID, amount)
                {
                    if (args.Length != 3) return false;
                    return DepositFrom((byte[])args[0], (byte[])args[1], (BigInteger)args[2]);
                }
                if (operation == "depositFromNonStandard") // (originator, assetID, amount)
                {
                    if (args.Length != 3) return false;
                    return DepositFromNonStandard((byte[])args[0], (byte[])args[1], (BigInteger)args[2]);
                }
                if (operation == "onTokenTransfer") // deposit for MCT contract only (from, to, amount)
                {
                    if (args.Length != 3) return false;
                    if (ExecutionEngine.CallingScriptHash != MctAssetID) return false;
                    if (ExecutionEngine.ExecutingScriptHash != (byte[])args[1]) return false;
                    if (!ReceivedNEP5((byte[])args[0], MctAssetID, (BigInteger)args[2])) throw new Exception("ReceivedNEP5 onTransfer failed!");
                    return true;
                }
                if (operation == "makeOffer") // (makerAddress, offerAssetID, offerAmount, wantAssetID, wantAmount, makerFeeAssetID, makerFeeAvailableAmount, nonce)
                {
                    if (GetState() != Active) return false;
                    if (args.Length != 8) return false;
                    var offer = NewOffer((byte[])args[0], (byte[])args[1], (byte[])args[2], (byte[])args[3], (byte[])args[4], (byte[])args[5], (byte[])args[6], (byte[])args[7]);
                    return MakeOffer(offer);
                }
                if (operation == "fillOffer") // (fillerAddress, offerHash, amountToTake, takerFeeAssetID, takerFeeAmount, burnTakerFees, makerFeeAmount, burnMakerFees)
                {
                    if (GetState() != Active) return false;
                    if (args.Length != 8) return false;
                    return FillOffer((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (byte[])args[3], (BigInteger)args[4], (bool)args[5], (BigInteger)args[6], (bool)args[7]);
                }
                if (operation == "cancelOffer") // (offerHash)
                {
                    if (GetState() == Pending) return false;
                    if (args.Length != 1) return false;
                    return CancelOffer((byte[])args[0]);
                }
                if (operation == "withdraw") // ()
                {
                    if (GetState() == Pending) return false;
                    return ProcessWithdrawal();
                }
                if (operation == "announceCancel") // (offerHash)
                {
                    if (GetState() == Pending) return false;
                    if (args.Length != 1) return false;
                    return AnnounceCancel((byte[])args[0]);
                }
                if (operation == "announceWithdraw") // (originator, assetID, amountToWithdraw)
                {
                    if (GetState() == Pending) return false;
                    if (args.Length != 3) return false;
                    return AnnounceWithdraw((byte[])args[0], (byte[])args[1], (BigInteger)args[2]);
                }
                if (operation == "burnTokens") // (originator, assetID, amountToBurn)
                {
                    if (GetState() == Pending) return false;
                    if (args.Length != 4) return false;
                    return BurnTokens((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (byte[])args[3]);
                }
                if (operation == "sweepDustTokens") // (originator, counterparty, dustAssetIDs, dustAmounts, combinedAssetID, combinedAmount)
                {
                    if (GetState() == Pending) return false;
                    if (args.Length != 6) return false;
                    return SweepDustTokens((byte[])args[0], (byte[])args[1], (byte[][])args[2], (BigInteger[])args[3], (byte[])args[4], (BigInteger)args[5]);
                }
                if (operation == "createAtomicSwap") // (makerAddress, takerAddress, assetID, amount, hashOfSecret, secondsToExpire, feeAssetID, feeAmount, burnTokens)
                {
                    if (GetState() == Pending) return false;
                    if (args.Length != 9) return false;
                    return CreateAtomicSwap((byte[])args[0], (byte[])args[1], (byte[])args[2], (BigInteger)args[3], (byte[])args[4], (BigInteger)args[5], (byte[])args[6], (BigInteger)args[7], (bool)args[8]);
                }
                if (operation == "executeAtomicSwap") // (hashOfSecret, preImage)
                {
                    if (GetState() == Pending) return false;
                    if (args.Length != 2) return false;
                    return ExecuteAtomicSwap((byte[])args[0], (byte[])args[1]);
                }
                if (operation == "cancelAtomicSwap") // (hashOfSecret, cancelFeeAmount, burnCancelFees)
                {
                    if (GetState() == Pending) return false;
                    if (args.Length != 3) return false;
                    return CancelAtomicSwap((byte[])args[0], (BigInteger)args[1], (bool)args[2]);
                }
                if (operation == "approveSpender") // (address, spender)
                {
                    if (GetState() == Pending) return false;
                    if (args.Length != 2) return false;
                    return ApproveSpender((byte[])args[0], (byte[])args[1]);
                }
                if (operation == "rescindApproval") // (address, spender)
                {
                    if (GetState() == Pending) return false;
                    if (args.Length != 2) return false;
                    return RescindApproval((byte[])args[0], (byte[])args[1]);
                }
                if (operation == "spendFrom") // (from, to, amount, assetID, decreaseFromReason, increaseToReason)
                {
                    if (GetState() == Pending) return false;
                    if (args.Length != 6) return false;
                    return SpendFrom((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (byte[])args[3], (byte[])args[4], (byte[])args[5], ExecutionEngine.CallingScriptHash);
                }

                // == Owner ==
                if (!Runtime.CheckWitness(Owner))
                {
                    Runtime.Log("Owner signature verification failed");
                    return false;
                }
                if (operation == "freezeTrading")
                {
                    if (!Runtime.CheckWitness(GetCoordinatorAddress())) return false; // ensure both coordinator and owner signed
                    Storage.Put(Context(), "state", Inactive);
                    EmitTradingFrozen();
                    return true;
                }
                if (operation == "unfreezeTrading")
                {
                    if (!Runtime.CheckWitness(GetCoordinatorAddress())) return false; // ensure both coordinator and owner signed
                    Storage.Put(Context(), "state", Active);
                    EmitTradingResumed();
                    return true;
                }
                if (operation == "setAnnounceDelay")
                {
                    if (args.Length != 1) return false;
                    return SetAnnounceDelay((BigInteger)args[0]);
                }
                if (operation == "setCoordinatorAddress")
                {
                    if (args.Length != 1) return false;
                    return SetCoordinatorAddress((byte[])args[0]);
                }
                if (operation == "setWithdrawCoordinatorAddress")
                {
                    if (args.Length != 1) return false;
                    return SetWithdrawCoordinatorAddress((byte[])args[0]);
                }
                if (operation == "setFeeAddress")
                {
                    if (args.Length != 1) return false;
                    return SetFeeAddress((byte[])args[0]);
                }
                if (operation == "addToWhitelist")
                {
                    if (args.Length != 2) return false;
                    return AddToWhitelist((byte[])args[0], (int)args[1]);
                }
                if (operation == "removeFromWhitelist")
                {
                    if (args.Length != 2) return false;
                    return RemoveFromWhitelist((byte[])args[0], (int)args[1]);
                }
                if (operation == "sealWhitelist")
                {
                    if (args.Length != 1) return false;
                    return SealWhitelist((int)args[0]);
                }
                if (operation == "addSpender")
                {
                    if (args.Length != 1) return false;
                    return AddSpender((byte[])args[0]);
                }
                if (operation == "removeSpender")
                {
                    if (args.Length != 1) return false;
                    return RemoveSpender((byte[])args[0]);
                }
            }

            return true;
        }

        /***********
         * Getters *
         ***********/

        private static byte[] GetState()
        {
            return Storage.Get(Context(), "state");
        }

        private static Map<byte[], BigInteger> GetBalances(byte[] address)
        {
            if (!IsAddressValid(address)) throw new ArgumentOutOfRangeException();
            return (Map<byte[], BigInteger>)Storage.Get(Context(), BalanceKey(address)).Deserialize();
        }

        private static BigInteger GetBalance(byte[] address, byte[] assetID)
        {
            if (!IsAssetLengthValid(assetID)) throw new ArgumentOutOfRangeException();
            Map<byte[], BigInteger> balances = GetBalances(address);
            if (balances.HasKey(assetID))
            {
                return GetBalances(address)[assetID];
            }
            else
            {
                return 0;
            }
        }

        private static bool GetIsWhitelisted(byte[] assetID, int whitelistEnum)
        {
            if (!IsToken(assetID)) throw new ArgumentOutOfRangeException();
            return Storage.Get(Context(), GetWhitelistKey(assetID, whitelistEnum)).Length > 0;
        }

        private static BigInteger GetAnnounceDelay()
        {
            return Storage.Get(Context(), "announceDelay").AsBigInteger();
        }

        private static byte[] GetFeeAddress()
        {
            return Storage.Get(Context(), "feeAddress");
        }

        private static byte[] GetCoordinatorAddress()
        {
            return Storage.Get(Context(), "coordinatorAddress");
        }

        private static byte[] GetWithdrawCoordinatorAddress()
        {
            return Storage.Get(Context(), "withdrawCoordinatorAddress");
        }

        private static Offer GetOffer(byte[] offerHash)
        {
            if (offerHash.Length != 32) throw new ArgumentOutOfRangeException();

            byte[] offerData = Storage.Get(Context(), OfferKey(offerHash));
            if (offerData.Length == 0) return new Offer();

            return (Offer)offerData.Deserialize();
        }

        private static Swap GetSwap(byte[] swapHash)
        {
            if (swapHash.Length != 32) throw new ArgumentOutOfRangeException();

            byte[] offerData = Storage.Get(Context(), SwapKey(swapHash));
            if (offerData.Length == 0) return new Swap();

            return (Swap)offerData.Deserialize();
        }

        private static AnnouncementInfo GetAnnouncedWithdrawal(byte[] withdrawingAddr, byte[] assetID)
        {
            var announce = Storage.Get(Context(), WithdrawAnnounceKey(withdrawingAddr, assetID));
            if (announce.Length == 0) return new AnnouncementInfo(); // not announced
            return (AnnouncementInfo)announce.Deserialize();
        }

        private static BigInteger GetAnnouncedCancel(byte[] offerHash)
        {
            var announceTime = Storage.Get(Context(), CancelAnnounceKey(offerHash));
            if (announceTime.Length == 0) return -1; // not announced
            return announceTime.AsBigInteger();
        }

        /***********
         * Control *
         ***********/

        private static bool Initialize(byte[] feeAddress, byte[] coordinatorAddress, byte[] withdrawCoordinatorAddress)
        {
            if (GetState() != Pending) return false;

            if (!SetFeeAddress(feeAddress)) throw new Exception("Failed to set fee address");
            if (!SetCoordinatorAddress(coordinatorAddress)) throw new Exception("Failed to set the coordinator");
            if (!SetWithdrawCoordinatorAddress(withdrawCoordinatorAddress)) throw new Exception("Failed to set the withdrawCoordinator");
            if (!SetAnnounceDelay(maxAnnounceDelay)) throw new Exception("Failed to announcement delay");

            Storage.Put(Context(), "state", Active);
            Initialized();

            return true;
        }

        private static bool SetFeeAddress(byte[] feeAddress)
        {
            if (!IsAddressValid(feeAddress)) return false;
            Storage.Put(Context(), "feeAddress", feeAddress);
            EmitFeeAddressSet(feeAddress);
            return true;
        }

        private static bool SetCoordinatorAddress(byte[] coordinatorAddress)
        {
            if (!IsAddressValid(coordinatorAddress)) return false;
            Storage.Put(Context(), "coordinatorAddress", coordinatorAddress);
            EmitCoordinatorSet(coordinatorAddress);
            return true;
        }

        private static bool SetWithdrawCoordinatorAddress(byte[] withdrawCoordinatorAddress)
        {
            if (!IsAddressValid(withdrawCoordinatorAddress)) return false;
            Storage.Put(Context(), "withdrawCoordinatorAddress", withdrawCoordinatorAddress);
            EmitWithdrawCoordinatorSet(withdrawCoordinatorAddress);
            return true;
        }

        private static bool SetAnnounceDelay(BigInteger delay)
        {
            if (delay < 0 || delay > maxAnnounceDelay) return false;
            Storage.Put(Context(), "announceDelay", delay);
            EmitAnnounceDelaySet(delay);
            return true;
        }

        private static bool AddToWhitelist(byte[] scriptHash, int whitelistEnum)
        {
            if (!IsToken(scriptHash)) return false;
            if (IsWhitelistSealed(whitelistEnum)) return false;
            var key = GetWhitelistKey(scriptHash, whitelistEnum);
            Storage.Put(Context(), key, Active);
            EmitAddedToWhitelist(scriptHash, whitelistEnum);
            return true;
        }

        private static bool RemoveFromWhitelist(byte[] scriptHash, int whitelistEnum)
        {
            if (!IsToken(scriptHash)) return false;
            if (IsWhitelistSealed(whitelistEnum)) return false;
            var key = GetWhitelistKey(scriptHash, whitelistEnum);
            Storage.Delete(Context(), key);
            EmitRemovedFromWhitelist(scriptHash, whitelistEnum);
            return true;
        }

        private static bool SealWhitelist(int whitelistEnum)
        {
            Storage.Put(Context(), GetWhitelistSealedKey(whitelistEnum), Active);
            EmitWhitelistSealed(whitelistEnum);
            return true;
        }

        /// @notice Adds an address to the set of allowed spenders.
        /// @dev Spenders are meant to be additional EVM contracts that
        /// will allow adding or upgrading of trading functionality, without
        /// having to cancel all offers and withdraw all tokens for all users.
        /// This whitelist ensures that all approved spenders are contracts
        /// that have been verified by the owner. Note that each user also
        /// has to invoke `approveSpender` to actually allow the `spender`
        /// to spend his/her balance, so that they can examine / verify
        /// the new spender contract first.
        /// @param spender The address to add as a whitelisted spender
        private static bool AddSpender(byte[] spender)
        {
            if (!IsAddressValid(spender)) return false;
            Storage.Put(Context(), WhitelistedSpenderKey(spender), Active);
            EmitSpenderAdded(spender);
            return true;
        }

        /// @notice Removes an address from the set of allowed spenders.
        /// @dev Note that removing a spender from the whitelist will not
        /// prevent already approved spenders from spending a user's balance.
        /// This is to ensure that the spender contracts can be certain that once
        /// an approval is done, the owner cannot rescient spending priviledges,
        /// and cause tokens to be withheld or locked in the spender contract.
        /// Users must instead manually rescind approvals using `rescindApproval`
        /// after the `spender` has been removed from the whitelist.
        /// @param spender The address to remove as a whitelisted spender
        private static bool RemoveSpender(byte[] spender)
        {
            if (!IsAddressValid(spender)) return false;
            Storage.Delete(Context(), WhitelistedSpenderKey(spender));
            EmitSpenderRemoved(spender);
            return true;
        }

        /***********
         * Spending contract balance from other contract/address *
         ***********/

        /// @notice Approve an address for spending any amount of
        /// any token from the `msg.sender`'s balances
        /// @dev Analogous to NEP-5 `approve`, with the following differences:
        ///     - `_spender` must be whitelisted by owner
        ///     - approval can be rescinded at a later time by the user
        ///       iff it has been removed from the whitelist
        ///     - spending amount is unlimited
        /// @param _spender The address to approve spending
        private static bool ApproveSpender(byte[] address, byte[] spender)
        {
            if (!IsAddressValid(spender)) return false;
            if (!Runtime.CheckWitness(address)) return false;
            // Check spender is whitelisted
            var isWhitelisted = Storage.Get(Context(), WhitelistedSpenderKey(spender));
            if (isWhitelisted != Active) return false;
            Storage.Put(Context(), ApproveSpenderKey(address, spender), Active);
            EmitSpenderApproved(address, spender);
            return true;
        }

        /// @notice Rescinds a previous approval for spending the `msg.sender`'s contract balance.
        /// @dev Rescinds approval for a spender, after it has been removed from
        /// the `whitelistedSpenders` set. This allows an approval to be removed
        /// if both the owner and user agrees that the previously approved spender
        /// contract should no longer be used.
        /// @param _spender The address to rescind spending approval
        private static bool RescindApproval(byte[] address, byte[] spender)
        {
            if (!IsAddressValid(spender)) return false;
            if (!Runtime.CheckWitness(address)) return false;
            // Check spender must be removed from the whitelist
            var isWhitelisted = Storage.Get(Context(), WhitelistedSpenderKey(spender));
            if (isWhitelisted == Active) return false;

            Storage.Delete(Context(), ApproveSpenderKey(address, spender));
            EmitSpenderRescinded(address, spender);
            return true;
        }

        /// @notice Transfers tokens from one address to another
        /// @dev Analogous to NEP-5 `transferFrom`, with the following differences:
        ///     - the address of the token to transfer must be specified
        ///     - any amount of token can be transferred, as long as it is less or equal
        ///       to `_from`'s balance
        ///     - reason codes can be attached and they must not use reasons specified in
        ///       this contract
        /// @param from The address to transfer tokens from
        /// @param to The address to transfer tokens to
        /// @param amount The number of tokens to transfer
        /// @param asset The address of the token to transfer
        /// @param decreaseReason A reason code to emit in the `BalanceDecrease` event
        /// @param increaseReason A reason code to emit in the `BalanceIncrease` event
        private static bool SpendFrom(byte[] from, byte[] to, BigInteger amount, byte[] assetID, byte[] decreaseFromReason, byte[] increaseToReason, byte[] callingScriptHash)
        {
            if (!IsValidUnusedReasonCode(decreaseFromReason)) return false;
            if (!IsValidUnusedReasonCode(increaseToReason)) return false;
            if (!IsAddressValid(from) || !IsAddressValid(to)) return false;
            if (!Runtime.CheckWitness(from)) return false;
            // Check from address approved spender
            var isApproved = Storage.Get(Context(), ApproveSpenderKey(from, callingScriptHash));
            if (isApproved != Active) return false;

            var balanceChanges = NewBalanceChanges();
            balanceChanges.ReduceBalance(from, assetID, amount, decreaseFromReason);
            balanceChanges.IncreaseBalance(to, assetID, amount, increaseToReason);
            ExecuteBalanceChanges(balanceChanges);
            return true;
        }

        /***********
         * Trading *
         ***********/

        private static bool MakeOffer(Offer offer)
        {
            // Check that transaction is signed by the maker and coordinator
            if (!CheckTradeWitnesses(offer.MakerAddress)) return false;

            // Check that nonce is not repeated
            var offerHash = Hash(offer);
            if (Storage.Get(Context(), OfferKey(offerHash)) != Empty) return false;

            // Check that the amounts > 0
            if (!(offer.OfferAmount > 0 && offer.WantAmount > 0)) return false;

            // Check the trade is across different assets
            if (offer.OfferAssetID == offer.WantAssetID) return false;

            // Check that asset IDs are valid
            if (!IsAssetLengthValid(offer.OfferAssetID) || !IsAssetLengthValid(offer.WantAssetID) || !IsAssetLengthValid(offer.MakerFeeAssetID)) return false;

            // Check fees
            if (offer.MakerFeeAvailableAmount < 0) return false;

            // Check that there are enough assets to be deducted for fees (This should still be true even if fee asset is same as offer asset)
            if (GetBalance(offer.MakerAddress, offer.MakerFeeAssetID) < offer.MakerFeeAvailableAmount) return false;

            var balanceChanges = NewBalanceChanges();
            // Reduce available balance for the offered asset and amount
            balanceChanges.ReduceBalance(offer.MakerAddress, offer.OfferAssetID, offer.OfferAmount, ReasonMakerGive);

            // Reserve fees from the maker fee asset only if it is different from want asset
            if (offer.MakerFeeAvailableAmount > 0 && offer.MakerFeeAssetID != offer.WantAssetID)
            {
                // Reduce fees here separately as it is a different asset type
                balanceChanges.ReduceBalance(offer.MakerAddress, offer.MakerFeeAssetID, offer.MakerFeeAvailableAmount, ReasonMakerFeeGive);
            }

            ExecuteBalanceChanges(balanceChanges);

            // Add the offer to storage
            StoreOffer(offerHash, offer);

            // Notify clients
            EmitCreated(offer.MakerAddress, offerHash, offer.OfferAssetID, offer.OfferAmount, offer.WantAssetID, offer.WantAmount);
            return true;
        }

        // Fills an offer by taking the amount you want
        // => amountToFill's asset type = offer's wantAssetID
        // amountToTake's asset type = offerAssetID (taker is taking what is offered)
        private static bool FillOffer(byte[] fillerAddress, byte[] offerHash, BigInteger amountToTake, byte[] takerFeeAssetID, BigInteger takerFeeAmount, bool burnTakerFee, BigInteger makerFeeAmount, bool burnMakerFee)
        {
            // Note: We do all checks first then execute state changes

            // Check that transaction is signed by the filler and coordinator
            if (!CheckTradeWitnesses(fillerAddress)) return false;

            // Check fees
            if (!IsAssetLengthValid(takerFeeAssetID)) return false;
            if (takerFeeAmount < 0) return false;
            if (makerFeeAmount < 0) return false;

            // Check that the offer still exists
            Offer offer = GetOffer(offerHash);
            if (offer.MakerAddress == Empty)
            {
                EmitFailed(fillerAddress, offerHash, amountToTake, takerFeeAssetID, takerFeeAmount, ReasonOfferNotExist);
                return false;
            }

            // Check that the filler is different from the maker
            if (fillerAddress == offer.MakerAddress)
            {
                EmitFailed(fillerAddress, offerHash, amountToTake, takerFeeAssetID, takerFeeAmount, ReasonFillerSameAsMaker);
                return false;
            }

            // Check that the amount that will be taken is at least 1
            if (amountToTake < 1)
            {
                EmitFailed(fillerAddress, offerHash, amountToTake, takerFeeAssetID, takerFeeAmount, ReasonTakingLessThanOne);
                return false;
            }

            // Check that you cannot take more than available
            if (amountToTake > offer.AvailableAmount)
            {
                EmitFailed(fillerAddress, offerHash, amountToTake, takerFeeAssetID, takerFeeAmount, ReasonTakingMoreThanAvailable);
                return false;
            }

            // Calculate amount we have to give the offerer (what the offerer wants)
            BigInteger amountToFill = (amountToTake * offer.WantAmount) / offer.OfferAmount;

            // Check that amount to fill(give) is not less than 1
            if (amountToFill < 1)
            {
                EmitFailed(fillerAddress, offerHash, amountToTake, takerFeeAssetID, takerFeeAmount, ReasonFillingLessThanOne);
                return false;
            }

            // Get all balances needed
            
            var fillerBalanceKey = BalanceKey(fillerAddress);
            var makerBalanceKey = BalanceKey(offer.MakerAddress);
            var fillerBalances = (Map<byte[], BigInteger>)Storage.Get(Context(), fillerBalanceKey).Deserialize();
            var makerBalances = (Map<byte[], BigInteger>)Storage.Get(Context(), makerBalanceKey).Deserialize();

            // Check that there is enough balance to reduce for filler
            if (!fillerBalances.HasKey(offer.WantAssetID))
            {
                EmitFailed(fillerAddress, offerHash, amountToTake, takerFeeAssetID, takerFeeAmount, ReasonNotEnoughBalanceOnFiller);
                return false;
            }

            var fillerOfferAssetBalance = fillerBalances[offer.WantAssetID];
            if (fillerOfferAssetBalance < amountToFill)
            {
                EmitFailed(fillerAddress, offerHash, amountToTake, takerFeeAssetID, takerFeeAmount, ReasonNotEnoughBalanceOnFiller);
                return false;
            }

            // Check if we should deduct fees separately from the taker amount
            bool deductTakerFeesSeparately = takerFeeAssetID != offer.OfferAssetID;

            // Check that there is enough balance in native fees if using native fees
            if (deductTakerFeesSeparately && takerFeeAmount > 0 && fillerBalances.HasKey(takerFeeAssetID) && fillerBalances[takerFeeAssetID] < takerFeeAmount)
            {
                EmitFailed(fillerAddress, offerHash, amountToTake, takerFeeAssetID, takerFeeAmount, ReasonNotEnoughBalanceOnNativeToken);
                return false;
            }

            // Check that maker fee does not exceed remaining amount that can be deducted as maker fees
            if (offer.MakerFeeAvailableAmount < makerFeeAmount)
            {
                EmitFailed(fillerAddress, offerHash, amountToTake, takerFeeAssetID, takerFeeAmount, ReasonNotEnoughBalanceOnRemainingMakerFee);
                return false;
            }

            // Check if we should deduct fees separately from the maker receiving amount
            bool deductMakerFeesSeparately = offer.MakerFeeAssetID != offer.WantAssetID;

            var balanceChanges = NewBalanceChanges();

            // Reduce balance from filler
            balanceChanges.ReduceBalance(fillerAddress, offer.WantAssetID, amountToFill, ReasonTakerGive);

            // Move filled asset to the maker balance (reduce fees if needed)
            var amountForMakerAfterFees = deductMakerFeesSeparately ? amountToFill : amountToFill - makerFeeAmount;
            balanceChanges.IncreaseBalance(offer.MakerAddress, offer.WantAssetID, amountForMakerAfterFees, ReasonMakerReceive);

            // Move taken asset to the taker balance
            var amountToTakeAfterFees = deductTakerFeesSeparately ? amountToTake : amountToTake - takerFeeAmount;
            balanceChanges.IncreaseBalance(fillerAddress, offer.OfferAssetID, amountToTakeAfterFees, ReasonTakerReceive);

            // Move fees
            if (takerFeeAmount > 0)
            {
                if (deductTakerFeesSeparately)
                {
                    // Reduce fees here from contract balance separately as it is a different asset type
                    fillerBalances[takerFeeAssetID] -= takerFeeAmount;
                    EmitTransferred(fillerAddress, takerFeeAssetID, 0 - takerFeeAmount, ReasonTakerFeeGive);
                }
                if (burnTakerFee)
                {
                    // Emit burnt event for easier client tracking
                    EmitBurnt(fillerAddress, takerFeeAssetID, takerFeeAmount);
                }
                else
                {
                    // Only increase fee address balance if not burning
                    balanceChanges.IncreaseBalance(GetFeeAddress(), takerFeeAssetID, takerFeeAmount, ReasonTakerFeeReceive);
                }
            }
            if (makerFeeAmount > 0)
            {
                // Reduce from available maker fees here
                offer.MakerFeeAvailableAmount = offer.MakerFeeAvailableAmount - makerFeeAmount;

                if (burnMakerFee)
                {
                    // Emit burnt event for easier client tracking
                    EmitBurnt(fillerAddress, offer.MakerFeeAssetID, makerFeeAmount);
                }
                else
                {
                    // Only increase fee address balance if not burning
                    balanceChanges.IncreaseBalance(GetFeeAddress(), offer.MakerFeeAssetID, makerFeeAmount, ReasonMakerFeeReceive);
                }
            }

            // Save balances
            ExecuteBalanceChanges(balanceChanges);

            // Update available amount
            offer.AvailableAmount = offer.AvailableAmount - amountToTake;

            // Store updated offer
            StoreOffer(offerHash, offer);

            // Notify clients
            EmitFilled(fillerAddress, offerHash, amountToFill, offer.OfferAssetID, offer.OfferAmount, offer.WantAssetID, offer.WantAmount, amountToTake);

            return true;
        }

        private static bool CancelOffer(byte[] offerHash)
        {
            // Check that the offer exists
            Offer offer = GetOffer(offerHash);
            if (offer.MakerAddress == Empty) return false;

            // Check that the transaction is signed by the coordinator or pre-announced
            var cancellationAnnounced = IsCancellationAnnounced(offerHash);
            var coordinatorWitnessed = Runtime.CheckWitness(GetCoordinatorAddress());
            if (!(coordinatorWitnessed || cancellationAnnounced)) return false;

            // Check that transaction is signed by the canceller or trading is frozen
            if (!(Runtime.CheckWitness(offer.MakerAddress) || (IsTradingFrozen() && coordinatorWitnessed))) return false;
            
            // Move tokens back to maker address
            var balanceChanges = NewBalanceChanges();
            balanceChanges.IncreaseBalance(offer.MakerAddress, offer.OfferAssetID, offer.AvailableAmount, ReasonCancel);

            // Check if maker fees were deducted from maker and reserved in offer separately
            bool deductMakerFeesSeparately = offer.MakerFeeAssetID != offer.WantAssetID;
            // Refund fees if fees were separately deducted
            if (deductMakerFeesSeparately && offer.MakerFeeAvailableAmount > 0)
            {
                balanceChanges.IncreaseBalance(offer.MakerAddress, offer.MakerFeeAssetID, offer.MakerFeeAvailableAmount, ReasonMakerFeeRefund);
            }

            ExecuteBalanceChanges(balanceChanges);

            // Remove offer
            RemoveOffer(offerHash);

            // Clean up announcement
            var key = CancelAnnounceKey(offerHash);
            if (key.Length > 0)
            {
                Storage.Delete(Context(), key);
            }

            // Notify runtime
            EmitCancelled(offer.MakerAddress, offerHash);
            return true;
        }

        private static bool AnnounceCancel(byte[] offerHash)
        {
            // Check that the offer exists
            Offer offer = GetOffer(offerHash);
            if (offer.MakerAddress == Empty) return false;

            // Check that transaction is signed by the canceller or trading is frozen
            if (!Runtime.CheckWitness(offer.MakerAddress)) return false;

            Storage.Put(Context(), CancelAnnounceKey(offerHash), Runtime.Time);

            // Announce cancel intent to coordinator
            EmitCancelAnnounced(offer.MakerAddress, offerHash);

            return true;
        }

        // Burn tokens directly by reducing the user's contract balance.
        private static bool BurnTokens(byte[] address, byte[] assetID, BigInteger amount, byte[] reasonCode)
        {
            // Check that burn txn is authorized
            if (!CheckTradeWitnesses(address)) return false;

            // Check that reason code is valid
            var code = reasonCode.ToBigInteger();
            if (code < 1 || code > 9) throw new ArgumentOutOfRangeException();

            // Reduce contract balance and emit burn event
            var balanceChanges = NewBalanceChanges();
            balanceChanges.ReduceBalance(address, assetID, amount, reasonCode);
            ExecuteBalanceChanges(balanceChanges);
            EmitBurnt(address, assetID, amount);

            return true;
        }

        // Swaps multiple dust tokens into a single token atomically. This operation has no fees.
        private static bool SweepDustTokens(byte[] originator, byte[] counterparty, byte[][] dustAssetIDs, BigInteger[] dustAmounts, byte[] combinedAssetID, BigInteger combinedAmount)
        {
            // Check that swap is signed by both the originator and the counterparty
            if (!CheckTradeWitnesses(originator)) return false;
            if (!Runtime.CheckWitness(counterparty)) return false;

            // Ensure parameters are correct
            if (!IsAddressValid(originator) || !IsAddressValid(counterparty) || !IsAssetLengthValid(combinedAssetID)) return false;
            if (dustAssetIDs.Length != dustAmounts.Length) return false;

            var balanceChanges = NewBalanceChanges();

            // Move dust tokens
            for (var i = 0; i < dustAssetIDs.Length; i++)
            {
                var assetID = dustAssetIDs[i];
                var amount = dustAmounts[i];

                if (amount < 1) throw new ArgumentOutOfRangeException();
                if (!IsAssetLengthValid(assetID)) throw new Exception("Must be a system asset or a token!");
                if (assetID == combinedAssetID) throw new Exception("Dust token must not be same as combined token!");

                balanceChanges.ReduceBalance(originator, assetID, amount, ReasonSweeperGive);
                balanceChanges.IncreaseBalance(counterparty, assetID, amount, ReasonSweepCounterpartyReceive);
            }

            // Move combined token
            if (combinedAmount < 1) throw new ArgumentOutOfRangeException();

            balanceChanges.ReduceBalance(counterparty, combinedAssetID, combinedAmount, ReasonSweepCounterpartyGive);
            balanceChanges.IncreaseBalance(originator, combinedAssetID, combinedAmount, ReasonSweeperReceive);

            // Save balances
            ExecuteBalanceChanges(balanceChanges);

            return true;
        }

        // Creates an atomic swap
        // Swap fee is just the fee charged in this blockchain
        // Whether swap fee is charged depends whether the swap creator is the taker or maker in the context of the trade and if fee is required in either case (Coordinator should take care of this)
        private static bool CreateAtomicSwap(byte[] makerAddress, byte[] takerAddress, byte[] assetID, BigInteger amount, byte[] hashedSecret, BigInteger expiryTime, byte[] feeAssetID, BigInteger feeAmount, bool burnTokens)
        {
            // Check that parameters are valid
            if (!IsAddressValid(makerAddress) || !IsAddressValid(takerAddress) || hashedSecret.Length != 32 || !IsAssetLengthValid(assetID)) return false;
            if (amount < 1 || feeAmount < 0 || expiryTime < Runtime.Time) return false;

            // Check that transaction is signed by maker and coordinator
            if (!CheckTradeWitnesses(makerAddress)) return false;

            // Check that there is no existing swap of the same hash
            var prevSwap = GetSwap(hashedSecret);
            if (prevSwap.MakerAddress.Length != 0) return false;

            var deductFeesSeparately = feeAssetID != assetID;
            
            // will deduct fees from locked asset
            if (!deductFeesSeparately && feeAmount > amount)
            {
                throw new Exception("Fee amount more than locking amount");
            }

            var balanceChanges = NewBalanceChanges();
            
            // Reduce contract balance from creator to lock in amount
            balanceChanges.ReduceBalance(makerAddress, assetID, amount, ReasonSwapMakerGive);

            // If fees are of a different asset, reduce fees from creator balance as this would represent his fees on this blockchain
            // else if fee is of the same asset, it will represent the counterparty's fee
            if (deductFeesSeparately && feeAmount > 0)
            {
                balanceChanges.ReduceBalance(makerAddress, feeAssetID, feeAmount, ReasonSwapMakerFeeGive);
            }

            // Store swap data
            var swap = new Swap
            {
                MakerAddress = makerAddress,
                TakerAddress = takerAddress,
                AssetID = assetID,
                Amount = amount,
                ExpiresAt = expiryTime,
                FeeAssetID = feeAssetID,
                FeeAmount = feeAmount,
                BurnTokens = burnTokens,
                Active = true
            };

            ExecuteBalanceChanges(balanceChanges);
            Storage.Put(Context(), SwapKey(hashedSecret), swap.Serialize());
            EmitSwapCreated(makerAddress, takerAddress, assetID, amount, hashedSecret, expiryTime, feeAssetID, feeAmount);

            return true;
        }

        private static bool ExecuteAtomicSwap(byte[] hashedSecret, byte[] preImage)
        {
            // Check that swap exists and is not already completed or cancelled
            var swap = GetSwap(hashedSecret);
            if (swap.MakerAddress.Length == 0 || !swap.Active) return false;

            // Verify pre-image
            if (Hash256(preImage).Reverse() != hashedSecret) return false;

            var balanceChanges = NewBalanceChanges();

            var deductFeesSeparately = swap.FeeAssetID != swap.AssetID;
            var amountForTakerAfterFees = deductFeesSeparately ? swap.Amount : swap.Amount - swap.FeeAmount;
            // Move tokens to the target address
            balanceChanges.IncreaseBalance(swap.TakerAddress, swap.AssetID, amountForTakerAfterFees, ReasonSwapTakerReceive);

            // Send fees to fee address
            if (swap.FeeAmount > 0)
            {
                if (swap.BurnTokens)
                {
                    // Emit burnt event for easier client tracking
                    EmitBurnt(swap.TakerAddress, swap.FeeAssetID, swap.FeeAmount);
                }
                else
                {
                    // Increase fee address balance with fees
                    balanceChanges.IncreaseBalance(GetFeeAddress(), swap.FeeAssetID, swap.FeeAmount, ReasonSwapFeeReceive);
                }
            }

            // Update that swap has been completed
            swap.Active = false;

            ExecuteBalanceChanges(balanceChanges);
            Storage.Put(Context(), SwapKey(hashedSecret), swap.Serialize());
            EmitSwapExecuted(hashedSecret);
            return true;
        }

        private static bool CancelAtomicSwap(byte[] hashOfSecret, BigInteger _cancelFeeAmount, bool burnCancelFee)
        {
            // Prevent double cancel. Check that swap exists and is not already completed or cancelled
            var swap = GetSwap(hashOfSecret);
            if (swap.MakerAddress.Length == 0 || !swap.Active) return false;

            // Check that the swap can be cancelled
            if (Runtime.Time < swap.ExpiresAt) return false;

            // Check that there is enough fees that was locked to deduct
            if (swap.FeeAmount < _cancelFeeAmount) return false;

            // Check that cancelFeeAmount is not < 0
            if (_cancelFeeAmount != null && _cancelFeeAmount < 0) return false;

            var deductFeesSeparately = swap.FeeAssetID != swap.AssetID;

            // Deduct full fees if not sent by coordinator
            var cancelFeeAmount = _cancelFeeAmount;
            if (!Runtime.CheckWitness(GetCoordinatorAddress()))
            {
                cancelFeeAmount = swap.FeeAmount;
            }

            var balanceChanges = NewBalanceChanges();

            // Return tokens to the original maker
            if (deductFeesSeparately)
            {
                // Return full amount locked
                balanceChanges.IncreaseBalance(swap.MakerAddress, swap.AssetID, swap.Amount, ReasonSwapCancelMakerReceive);

                // Deduct cancel fee from swap.FeeAmount to get the amount to refund
                var feesToRefund = swap.FeeAmount - cancelFeeAmount;
                if (feesToRefund > 0)
                {
                    balanceChanges.IncreaseBalance(swap.MakerAddress, swap.FeeAssetID, feesToRefund, ReasonSwapCancelFeeRefundReceive);
                }
            }
            else
            {
                // Deduct cancel fees directly from refund amount
                var amountToRefundAfterFees = swap.Amount - cancelFeeAmount; // cancel fee is already checked to be less than feeAmount above
                // Refund remaining swap amount
                balanceChanges.IncreaseBalance(swap.MakerAddress, swap.AssetID, amountToRefundAfterFees, ReasonSwapCancelMakerReceive);
            }

            // Send cancel fees to fee address
            if (cancelFeeAmount > 0)
            {
                if (burnCancelFee)
                {
                    // Emit burnt event for easier client tracking
                    EmitBurnt(swap.MakerAddress, swap.FeeAssetID, cancelFeeAmount);
                }
                else
                {
                    // Increase fee address balance with fees
                    balanceChanges.IncreaseBalance(GetFeeAddress(), swap.FeeAssetID, cancelFeeAmount, ReasonSwapCancelFeeReceive);
                }
            }

            // Update that swap has been cancelled
            swap.Active = false;
            ExecuteBalanceChanges(balanceChanges);
            Storage.Put(Context(), SwapKey(hashOfSecret), swap.Serialize());
            EmitSwapCancelled(hashOfSecret, cancelFeeAmount);

            return true;
        }

        private static void StoreOffer(byte[] offerHash, Offer offer)
        {
            // Remove offer if completely filled
            if (offer.AvailableAmount == 0)
            {
                RemoveOffer(offerHash);
            }
            else if (offer.AvailableAmount < 0)
            {
                throw new Exception("Invalid offer available amount!");
            }
            // Store offer otherwise
            else
            {
                // Serialize offer
                var offerData = offer.Serialize();
                Storage.Put(Context(), OfferKey(offerHash), offerData);
            }
        }

        private static void RemoveOffer(byte[] offerHash)
        {
            // Delete offer data
            Storage.Delete(Context(), OfferKey(offerHash));
        }

        /***********
         * Deposit *
         ***********/

        private static bool Deposit(byte[] originator, byte[] assetID, BigInteger amount)
        {
            // Check asset lengths
            if (IsSystemAsset(assetID))
            {
                // Accept all system assets
                var received = Received();
                // Mark deposit
                var currentTxn = (Transaction)ExecutionEngine.ScriptContainer;
                Storage.Put(Context(), DepositKey(currentTxn), 1);
                return received;
            }
            else if (IsToken(assetID))
            {
                // Update balances first
                if (!ReceivedNEP5(originator, assetID, amount)) return false;

                // Execute deposit to our contract (ExecutionEngine.ExecutingScriptHash)
                TransferNEP5(originator, ExecutionEngine.ExecutingScriptHash, assetID, amount);

                return true;
            }

            // Unknown asset category
            return false;
        }

        private static bool DepositFrom(byte[] originator, byte[] assetID, BigInteger amount)
        {
            // Check asset length
            if (!IsToken(assetID)) return false;

            // Update balances first
            if (!ReceivedNEP5(originator, assetID, amount)) return false;

            // Execute deposit to our contract (ExecutionEngine.ExecutingScriptHash)
            TransferFromNEP5(originator, ExecutionEngine.ExecutingScriptHash, assetID, amount);

            return true;
        }

        private static bool DepositFromNonStandard(byte[] originator, byte[] assetID, BigInteger amount)
        {
            // Check asset length
            if (!IsToken(assetID)) return false;

            // Update balances first
            if (!ReceivedNEP5(originator, assetID, amount)) return false;

            // Execute deposit to our contract (ExecutionEngine.ExecutingScriptHash)
            TransferFromNonStandardNEP5(originator, ExecutionEngine.ExecutingScriptHash, assetID, amount);

            return true;
        }

        // Received NEP-5 tokens
        private static bool ReceivedNEP5(byte[] originator, byte[] assetID, BigInteger amount)
        {
            // Verify that deposit is authorized
            if (!CheckTradeWitnesses(originator)) return false;
            if (GetState() != Active) return false;
            // Check address and amounts
            if (!IsAddressValid(originator)) return false;
            if (amount < 1) return false;
            // Update balances first
            var balanceChanges = NewBalanceChanges();
            balanceChanges.IncreaseBalance(originator, assetID, amount, ReasonDeposit);
            ExecuteBalanceChanges(balanceChanges);
            EmitDeposited(originator, assetID, amount);

            return true;
        }

        // Received system asset
        public static bool Received()
        {
            // Check the current transaction for the system assets
            var currentTxn = (Transaction)ExecutionEngine.ScriptContainer;
            var outputs = currentTxn.GetOutputs();
            var references = currentTxn.GetReferences();

            // Check if existing deposit flag is present
            if (Storage.Get(Context(), DepositKey(currentTxn)).Length > 0) return false;

            // Don't deposit if this is a withdrawal
            var coordinatorAddress = GetCoordinatorAddress();
            var withdrawCoordinatorAddress = GetWithdrawCoordinatorAddress();
            foreach (var i in references)
            {
                if (i.ScriptHash == ExecutionEngine.ExecutingScriptHash) return false;
                if (i.ScriptHash == coordinatorAddress) return false;
                if (i.ScriptHash == withdrawCoordinatorAddress) return false;
            }

            // Only deposit those assets not from contract
            ulong sentGasAmount = 0;
            ulong sentNeoAmount = 0;

            foreach (var o in outputs)
            {
                if (o.ScriptHash == ExecutionEngine.ExecutingScriptHash)
                {
                    if (o.AssetId == GasAssetID)
                    {
                        sentGasAmount += (ulong)o.Value;
                    }
                    else if (o.AssetId == NeoAssetID)
                    {
                        sentNeoAmount += (ulong)o.Value;
                    }
                }
            }

            var balanceChanges = NewBalanceChanges();
            byte[] firstAvailableAddress = references[0].ScriptHash;
            if (sentGasAmount > 0)
            {
                balanceChanges.IncreaseBalance(firstAvailableAddress, GasAssetID, sentGasAmount, ReasonDeposit);
                EmitDeposited(firstAvailableAddress, GasAssetID, sentGasAmount);
            }
            if (sentNeoAmount > 0)
            {
                balanceChanges.IncreaseBalance(firstAvailableAddress, NeoAssetID, sentNeoAmount, ReasonDeposit);
                EmitDeposited(firstAvailableAddress, NeoAssetID, sentNeoAmount);
            }

            ExecuteBalanceChanges(balanceChanges);

            return true;
        }

        /**************
         * Withdrawal *
         **************/

        private static bool VerifyWithdrawalValid(byte[] holderAddress, byte[] assetID, BigInteger amount)
        {
            if (amount < 1) return false;

            var balance = GetBalance(holderAddress, assetID);
            if (balance < amount) return false;

            return true;
        }

        private static bool AnnounceWithdraw(byte[] originator, byte[] assetID, BigInteger amountToWithdraw)
        {
            if (!Runtime.CheckWitness(originator)) return false;

            if (!VerifyWithdrawalValid(originator, assetID, amountToWithdraw)) return false;

            AnnouncementInfo withdrawInfo = new AnnouncementInfo
            {
                TimeStamp = Runtime.Time,
                Amount = amountToWithdraw
            };

            var key = WithdrawAnnounceKey(originator, assetID);
            Storage.Put(Context(), key, withdrawInfo.Serialize());

            // Announce withdrawal intent to clients
            EmitWithdrawAnnounced(originator, assetID, amountToWithdraw);

            return true;
        }

        private static object ProcessWithdrawal()
        {
            var currentTxn = (Transaction)ExecutionEngine.ScriptContainer;
            var withdrawalStage = GetWithdrawalStage(currentTxn);
            var withdrawingAddr = GetWithdrawalAddress(currentTxn); // Not validated, anyone can help anyone do step 2 of withdrawal
            var assetID = GetWithdrawalAsset(currentTxn);
            var isWithdrawingNEP5 = IsToken(assetID);

            if (withdrawalStage == Mark)
            {
                if (!Runtime.CheckWitness(ExecutionEngine.ExecutingScriptHash)) return false;
                var outputs = currentTxn.GetOutputs();
                BigInteger amount = outputs[0].Value;

                bool withdrawalAnnounced = IsWithdrawalAnnounced(withdrawingAddr, assetID, amount);

                // Only pass if withdraw coordinator signed or withdrawal is announced
                if (!(Runtime.CheckWitness(GetWithdrawCoordinatorAddress()) || withdrawalAnnounced))
                {
                    Runtime.Log("Withdraw coordinator witness missing or withdrawal unannounced");
                    return false;
                }

                // Check again that: amount > 0, balance enough and whether there is an existing withdraw as withdraw can only happen 1 at a time for each asset
                // Because things might have changed between verification phase and application phase
                if (!VerifyWithdrawalValid(withdrawingAddr, assetID, amount))
                {
                    Runtime.Log("Verify withdrawal failed");
                    return false;
                }

                // Attempt to reduce contract balance
                var balanceChanges = NewBalanceChanges();
                balanceChanges.ReduceBalance(withdrawingAddr, assetID, amount, ReasonWithdrawal);

                // Clear withdrawing announcement by user for this asset if any withdrawal is successful because it would mean that withdrawal is working correctly and exchange is in action
                var key = WithdrawAnnounceKey(withdrawingAddr, assetID);
                if (key.Length > 0)
                {
                    Storage.Delete(Context(), key);
                }

                // Reserve the transaction hash for the user
                Storage.Put(Context(), WithdrawalAddressKey(currentTxn.Hash), withdrawingAddr);

                // Notify clients
                EmitWithdrawing(withdrawingAddr, assetID, amount);

                return true;
            }

            if (withdrawalStage == Withdraw)
            {
                if (isWithdrawingNEP5)
                {
                    // Check withdrawal validity
                    var amount = GetWithdrawalAmount(currentTxn);
                    if (amount <= 0) return false;

                    // Check that the withdrawing address has sufficient contract balance
                    if (!VerifyWithdrawalValid(withdrawingAddr, assetID, amount)) return false;

                    // Check that the transaction is signed by the withdraw coordinator, or...
                    if (!Runtime.CheckWitness(GetWithdrawCoordinatorAddress()))
                    {
                        // If not signed by withdraw coordinator it must be pre-announced + signed by the user
                        if (!Runtime.CheckWitness(withdrawingAddr)) return false;
                        if (!IsWithdrawalAnnounced(withdrawingAddr, assetID, amount)) return false;
                    }

                    // Check old whitelist
                    if (IsWhitelistedOldNEP5(assetID))
                    {
                        // This contract must pass witness for old NEP-5 transfer to succeed
                        if (!Runtime.CheckWitness(ExecutionEngine.ExecutingScriptHash)) return false;
                    }
                    // Check new whitelist
                    else
                    {
                        // New-style NEP-5 transfers or arbitrary invokes SHOULD NOT pass this contract's witness checks
                        if (Runtime.CheckWitness(ExecutionEngine.ExecutingScriptHash)) return false;
                    }

                    // Attempt to reduce contract balance
                    var balanceChanges = NewBalanceChanges();
                    balanceChanges.ReduceBalance(withdrawingAddr, assetID, amount, ReasonWithdrawal);

                    // Execute withdrawal
                    TransferNEP5(ExecutionEngine.ExecutingScriptHash, withdrawingAddr, assetID, amount);

                    ExecuteBalanceChanges(balanceChanges);

                    // Notify clients
                    EmitWithdrawing(withdrawingAddr, assetID, amount);
                    EmitWithdrawn(withdrawingAddr, assetID, amount, currentTxn.Hash);
                }
                else
                {
                    var inputs = currentTxn.GetInputs();
                    var references = currentTxn.GetReferences();

                    // Clean up reservations
                    Storage.Delete(Context(), WithdrawalAddressKey(inputs[0].PrevHash));

                    // Notify clients
                    EmitWithdrawn(withdrawingAddr, assetID, references[0].Value, inputs[0].PrevHash);
                }

                return true;
            }

            return false;
        }

        private static byte[] GetWithdrawalAddress(Transaction transaction)
        {
            var txnAttributes = transaction.GetAttributes();
            foreach (var attr in txnAttributes)
            {
                if (attr.Usage == TAUsage_WithdrawalAddress) return attr.Data.Take(20);
            }
            throw new ArgumentNullException();
        }

        private static byte[] GetWithdrawalAsset(Transaction transaction)
        {
            var txnAttributes = transaction.GetAttributes();
            foreach (var attr in txnAttributes)
            {
                if (attr.Usage == TAUsage_NEP5AssetID) return attr.Data.Take(20);
                if (attr.Usage == TAUsage_SystemAssetID) return attr.Data.Take(32);
            }
            throw new ArgumentNullException();
        }

        private static BigInteger GetWithdrawalAmount(Transaction transaction)
        {
            var txnAttributes = transaction.GetAttributes();
            foreach (var attr in txnAttributes)
            {
                if (attr.Usage == TAUsage_WithdrawalAmount) return attr.Data.Take(32).Concat(Zero).AsBigInteger();
            }
            throw new ArgumentNullException();
        }

        private static byte[] GetWithdrawalStage(Transaction transaction)
        {
            var txnAttributes = transaction.GetAttributes();
            foreach (var attr in txnAttributes)
            {
                if (attr.Usage == TAUsage_WithdrawalStage) return attr.Data.Take(1);
            }
            throw new ArgumentNullException();
        }

        private static ulong SumOutputAmounts(TransactionOutput[] outputs, byte[] address, byte[] assetID)
        {
            ulong totalOut = 0;
            foreach (var o in outputs)
            {
                if (o.ScriptHash == address && o.AssetId == assetID)
                {
                    totalOut += (ulong)o.Value;
                }
            }
            return totalOut;
        }

        // Helpers
        private static StorageContext Context() => Storage.CurrentContext;
        private static BigInteger AmountToOffer(Offer o, BigInteger amount) => (o.OfferAmount * amount) / o.WantAmount;
        private static bool IsTradingFrozen() => Storage.Get(Context(), "state") == Inactive;

        private static bool IsAllInputsUnreserved(TransactionInput[] inputs)
        {
            foreach (var i in inputs)
            {
                if (i.PrevIndex == 0 && Storage.Get(Context(), WithdrawalAddressKey(i.PrevHash)).Length > 0) return false;
            }

            return true;
        }

        private static bool IsFullTransferToSelf(TransactionOutput[] inputs, TransactionOutput[] outputs, byte[] assetID)
        {
            ulong totalIn = SumOutputAmounts(inputs, ExecutionEngine.ExecutingScriptHash, assetID);
            ulong totalOut = SumOutputAmounts(outputs, ExecutionEngine.ExecutingScriptHash, assetID);
            return totalIn == totalOut;
        }

        private static bool IsWithdrawalAnnounced(byte[] withdrawingAddr, byte[] assetID, BigInteger amount)
        {
            var announce = Storage.Get(Context(), WithdrawAnnounceKey(withdrawingAddr, assetID));
            if (announce.Length == 0) return false; // not announced
            var announceInfo = (AnnouncementInfo)announce.Deserialize();
            var announceDelay = GetAnnounceDelay();

            return announceInfo.TimeStamp + announceDelay < Runtime.Time && announceInfo.Amount == amount;
        }

        private static bool IsCancellationAnnounced(byte[] offerHash)
        {
            var announceTime = Storage.Get(Context(), CancelAnnounceKey(offerHash));
            if (announceTime.Length == 0) return false; // not announced
            var announceDelay = GetAnnounceDelay();

            return announceTime.AsBigInteger() + announceDelay < Runtime.Time;
        }

        private static bool IsWhitelistedOldNEP5(byte[] assetID)
        {
            if (!IsToken(assetID)) return false;
            if (assetID.AsBigInteger() == 0) return false;
            return Storage.Get(Context(), OldWhitelistKey(assetID)).Length > 0;
        }

        private static bool IsWhitelistedNewNEP5(byte[] assetID)
        {
            if (!IsToken(assetID)) return false;
            if (assetID.AsBigInteger() == 0) return false;
            return Storage.Get(Context(), NewWhitelistKey(assetID)).Length > 0;
        }

        private static bool IsWhitelistSealed(int whitelistEnum)
        {
            return Storage.Get(Context(), GetWhitelistSealedKey(whitelistEnum)).Length > 0;
        }

        private static bool CheckTradeWitnesses(byte[] traderAddress)
        {
            // Cache coordinator address for checks
            var coordinatorAddress = GetCoordinatorAddress();

            // Check that transaction is signed by the trader
            if (!Runtime.CheckWitness(traderAddress)) return false;

            // Check that transaction is signed by the coordinator
            if (!Runtime.CheckWitness(coordinatorAddress)) return false;

            // Check that the trader is not also the coordinator
            if (traderAddress == coordinatorAddress) return false;

            // Check that the trader is not also the withdrawCoordinator
            if (traderAddress == GetWithdrawCoordinatorAddress()) return false;

            return true;
        }

        private static void TransferNEP5(byte[] from, byte[] to, byte[] assetID, BigInteger amount)
        {
            // Transfer token
            var args = new object[] { from, to, amount };
            var contract = (NEP5Contract)assetID.ToDelegate();
            if (!(bool)contract("transfer", args)) throw new Exception("Failed to transfer NEP-5 tokens!");
        }

        private static void TransferFromNEP5(byte[] from, byte[] to, byte[] assetID, BigInteger amount)
        {
            // Transfer token (using pre-approval)
            var args = new object[] { ExecutionEngine.ExecutingScriptHash, from, to, amount };
            var contract = (NEP5Contract)assetID.ToDelegate();
            if (!(bool)contract("transferFrom", args)) throw new Exception("Failed to transfer NEP-5 tokens!");
        }

        private static void TransferFromNonStandardNEP5(byte[] from, byte[] to, byte[] assetID, BigInteger amount)
        {
            // Transfer token (using pre-approval for non-standard contracts)
            var args = new object[] { from, to, amount };
            var contract = (NEP5Contract)assetID.ToDelegate();
            if (!(bool)contract("transferFrom", args)) throw new Exception("Failed to transfer NEP-5 tokens!");
        }

        private static byte[] GetWhitelistKey(byte[] assetID, int whitelistEnum)
        {
            if (whitelistEnum == 0) return OldWhitelistKey(assetID);
            if (whitelistEnum == 1) return NewWhitelistKey(assetID);
            throw new ArgumentOutOfRangeException();
        }

        private static byte[] GetWhitelistSealedKey(int whitelistEnum)
        {
            if (whitelistEnum == 0) return "oldWhitelistSealed".AsByteArray();
            if (whitelistEnum == 1) return "newWhitelistSealed".AsByteArray();
            throw new ArgumentOutOfRangeException();
        }

        private static bool IsToken(byte[] scriptHash)
        {
            return scriptHash.Length == 20;
        }

        private static bool IsSystemAsset(byte[] scriptHash)
        {
            return scriptHash.Length == 32;
        }
        private static bool IsAssetLengthValid(byte[] scriptHash)
        {
            return IsToken(scriptHash) || IsSystemAsset(scriptHash);
        }

        private static bool IsAddressValid(byte[] scriptHash)
        {
            return scriptHash.Length == 20;
        }

        private static bool IsValidUnusedReasonCode(byte[] reasonCode)
        {
            return reasonCode.Length == 1 && !(reasonCode[0] <= 0x3D);
        }

        private static byte[] Hash(Offer o)
        {
            var offerAmountBytes = o.OfferAmount.AsByteArray();
            var wantAmountBytes = o.WantAmount.AsByteArray();

            if (offerAmountBytes.Length > 32 || wantAmountBytes.Length > 32) throw new ArgumentOutOfRangeException();

            var bytes = o.MakerAddress
                .Concat(o.OfferAssetID)
                .Concat(o.WantAssetID)
                .Concat(offerAmountBytes.Concat(Zeroes).Take(32))
                .Concat(wantAmountBytes.Concat(Zeroes).Take(32))
                .Concat(o.Nonce);

            return Hash256(bytes);
        }

        // Keys
        private static byte[] OfferKey(byte[] offerHash) => "offers".AsByteArray().Concat(offerHash);
        private static byte[] SwapKey(byte[] swapHash) => "swaps".AsByteArray().Concat(swapHash);
        private static byte[] BalanceKey(byte[] originator) => "balances".AsByteArray().Concat(originator);
        private static byte[] WithdrawalAddressKey(byte[] transactionHash) => "withdrawUTXO".AsByteArray().Concat(transactionHash);
        private static byte[] OldWhitelistKey(byte[] assetID) => "oldNEP5Whitelist".AsByteArray().Concat(assetID);
        private static byte[] NewWhitelistKey(byte[] assetID) => "newNEP5Whitelist".AsByteArray().Concat(assetID);
        private static byte[] DepositKey(Transaction txn) => "deposited".AsByteArray().Concat(txn.Hash);
        private static byte[] CancelAnnounceKey(byte[] offerHash) => "cancelAnnounce".AsByteArray().Concat(offerHash);
        private static byte[] WithdrawAnnounceKey(byte[] originator, byte[] assetID) => "withdrawAnnounce".AsByteArray().Concat(originator).Concat(assetID);
        private static byte[] WhitelistedSpenderKey(byte[] spender) => "whitelistedSpenders".AsByteArray().Concat(spender);
        private static byte[] ApproveSpenderKey(byte[] originator, byte[] spender) => "approveSpenders".AsByteArray().Concat(originator).Concat(spender);
    }
}
