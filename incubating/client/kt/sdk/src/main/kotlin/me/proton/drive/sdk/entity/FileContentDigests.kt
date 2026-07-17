package me.proton.drive.sdk.entity

data class FileContentDigests(
    val sha1: ByteArray?,
    val sha1Verified: Boolean,
) {
    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (javaClass != other?.javaClass) return false

        other as FileContentDigests

        if (sha1Verified != other.sha1Verified) return false
        if (!sha1.contentEquals(other.sha1)) return false

        return true
    }

    override fun hashCode(): Int {
        var result = sha1Verified.hashCode()
        result = 31 * result + (sha1?.contentHashCode() ?: 0)
        return result
    }
}
