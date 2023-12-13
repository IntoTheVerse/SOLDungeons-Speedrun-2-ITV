import NonFungibleToken from 0x631e88ae7f1d7c20

pub contract DunCharacterNFT: NonFungibleToken {

  pub var totalSupply: UInt64
  pub var characterMetadata: {UInt64: String} 

  pub event ContractInitialized()
  pub event Withdraw(id: UInt64, from: Address?)
  pub event Deposit(id: UInt64, to: Address?)

  pub resource NFT: NonFungibleToken.INFT {
    pub let id: UInt64 
    pub var metadata: String

    init(id: UInt64, metadata: String) {
      self.id = id
      DunCharacterNFT.totalSupply = DunCharacterNFT.totalSupply + 1
      
      self.metadata = metadata
    }
  }

  pub resource interface CollectionPublic {
    pub fun borrowEntireNFT(id: UInt64): &DunCharacterNFT.NFT
  }

  pub resource Collection: NonFungibleToken.Receiver, NonFungibleToken.Provider, NonFungibleToken.CollectionPublic, CollectionPublic {
    pub var ownedNFTs: @{UInt64: NonFungibleToken.NFT}

    pub fun deposit(token: @NonFungibleToken.NFT) {
      let myToken <- token as! @DunCharacterNFT.NFT
      emit Deposit(id: myToken.id, to: self.owner?.address)
      self.ownedNFTs[myToken.id] <-! myToken
    }

    pub fun withdraw(withdrawID: UInt64): @NonFungibleToken.NFT {
      let token <- self.ownedNFTs.remove(key: withdrawID) ?? panic("This NFT does not exist")
      emit Withdraw(id: token.id, from: self.owner?.address)
      return <- token
    }

    pub fun getIDs(): [UInt64] {
      return self.ownedNFTs.keys
    }

    pub fun borrowNFT(id: UInt64): &NonFungibleToken.NFT {
      return (&self.ownedNFTs[id] as &NonFungibleToken.NFT?)!
    }

    pub fun borrowEntireNFT(id: UInt64): &DunCharacterNFT.NFT {
      let reference = (&self.ownedNFTs[id] as auth &NonFungibleToken.NFT?)!
      return reference as! &DunCharacterNFT.NFT
    }

    init() {
      self.ownedNFTs <- {}
    }

    destroy() {
      destroy self.ownedNFTs
    }
  }

  pub fun createEmptyCollection(): @Collection {
    return <- create Collection()
  }

  pub fun createToken(id: UInt64): @DunCharacterNFT.NFT {
    let characterMetadata: String = self.characterMetadata[id] ?? panic("Can't get Metadata")
    return <- create NFT(id: id, metadata: characterMetadata)
  }

  init() {
    self.totalSupply = 0

    self.characterMetadata = {
      1: "{\"Name\" : \"Bob\", \"Price\" : 12}",
      2: "{\"Name\" : \"Chris\", \"Price\" : 20}"
    }
  }
}