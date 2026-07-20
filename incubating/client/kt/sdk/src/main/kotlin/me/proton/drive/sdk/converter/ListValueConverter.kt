package me.proton.drive.sdk.converter

import com.google.protobuf.Any
import com.google.protobuf.ListValue

class ListValueConverter : AnyConverter<List<String>> {
    override val typeUrl: String = "type.googleapis.com/google.protobuf.ListValue"

    override fun convert(any: Any): List<String> =
        ListValue.parseFrom(any.value).valuesList.map { it.stringValue }
}
