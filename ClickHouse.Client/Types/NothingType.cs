﻿using System;

namespace ClickHouse.Client.Types
{
    internal class NothingType : ClickHouseType
    {
        public override ClickHouseTypeCode DataType => ClickHouseTypeCode.Nothing;

        public override Type EquivalentType => typeof(DBNull);

        public override string ToString() => "Nothing";
    }
}