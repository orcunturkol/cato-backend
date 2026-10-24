# The live resources, created before this state existed (Phase 2 step 1). `terraform plan`
# shows the imports; `terraform apply` writes them into the state and changes nothing in AWS.
import {
  to = aws_db_subnet_group.cato
  id = "cato-rds-subnet-group"
}

import {
  to = aws_security_group.rds
  id = "sg-02821bdf7d5512bc6"
}

import {
  to = aws_db_instance.cato
  id = "cato-postgres"
}

import {
  to = aws_vpc_security_group_egress_rule.rds_all
  id = "sgr-021bd977ec4b4434f"
}
